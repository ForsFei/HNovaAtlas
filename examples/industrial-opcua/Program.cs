using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using IndustrialOpcUa;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Client;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

if (args.Length != 1 || args[0] is not ("--self-test" or "--demo"))
{
    Console.WriteLine("Use --self-test or --demo. Own loopback server only; no external endpoint option.");
    return 2;
}

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    checks++;
    Console.WriteLine($"PASS {checks}: {name}");
}

// Reserve then release a loopback port: startup still fails if another process wins the handoff.
int port;
using (var reservation = new TcpListener(IPAddress.Loopback, 0))
{
    reservation.Start();
    port = ((IPEndPoint)reservation.LocalEndpoint).Port;
}
string endpoint = $"opc.tcp://127.0.0.1:{port}/HNovaDemo";
string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
string runRoot = Path.Combine(project, ".local", Guid.NewGuid().ToString("N"));
string serverPki = Path.Combine(runRoot, "server");
string clientPki = Path.Combine(runRoot, "client");
Console.WriteLine($"Own loopback endpoint: {endpoint}\nIsolated PKI (contains private keys; ignored by Git): {runRoot}");
using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
CancellationToken ct = budget.Token;
try
{
    // Boot once with empty trust stores. A certificate/security failure is required.
    using (IHost server = DemoHosts.Server(endpoint, serverPki))
    using (IHost client = DemoHosts.Client(endpoint, clientPki))
    {
        try
        {
            await DemoHosts.StartServerAsync(server, ct);
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(x => x.Port == port).ToArray();
            Console.WriteLine("Actual listeners: " + string.Join(", ", listeners.Select(x => x.ToString())));
            Check(listeners.Length > 0 && listeners.All(x => IPAddress.IsLoopback(x.Address)), "Server listener is loopback only");
            Console.WriteLine("Untrusted-stage server started.");
            await client.StartAsync(ct);
            Console.WriteLine("Untrusted-stage client initialized.");
            var connect = client.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            try
            {
                await using ManagedSession unexpected = await connect(ct);
                throw new InvalidOperationException("Untrusted peer unexpectedly connected.");
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadCertificateUntrusted ||
                                                    ex.StatusCode == StatusCodes.BadSecurityChecksFailed ||
                                                    ex.StatusCode == StatusCodes.BadCertificateInvalid)
            {
                Check(true, $"Untrusted certificates rejected ({ex.StatusCode})");
            }
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
            await server.StopAsync(CancellationToken.None);
        }
    }
    Check(DemoHosts.TrustOwnPeer(clientPki, serverPki, "HNovaOpcUaClient") > 0, "Explicit client leaf trust");

    // Server trusts client, but client still must reject this untrusted server.
    using (IHost server = DemoHosts.Server(endpoint, serverPki))
    using (IHost client = DemoHosts.Client(endpoint, clientPki))
    {
        try
        {
            await DemoHosts.StartServerAsync(server, ct);
            await client.StartAsync(ct);
            var connect = client.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            try
            {
                await using ManagedSession unexpected = await connect(ct);
                throw new InvalidOperationException("Client accepted untrusted server.");
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                Check(true, "Client independently refuses untrusted server");
            }
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
            await server.StopAsync(CancellationToken.None);
        }
    }
    Check(DemoHosts.TrustOwnPeer(serverPki, clientPki, "HNovaOpcUaServer") > 0, "Explicit server leaf trust");

    // New hosts reload the approved stores; no validation callback is overridden.
    using IHost trustedServer = DemoHosts.Server(endpoint, serverPki);
    using IHost trustedClient = DemoHosts.Client(endpoint, clientPki);
    try
    {
        await DemoHosts.StartServerAsync(trustedServer, ct);
        await trustedClient.StartAsync(ct);
        var connect = trustedClient.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
        await using ManagedSession session = await connect(ct);
        Check(session.Connected, "Connected session");
        Check(session.ConfiguredEndpoint!.Description.SecurityMode == MessageSecurityMode.SignAndEncrypt, "SignAndEncrypt");
        Check(session.ConfiguredEndpoint.Description.SecurityPolicyUri == SecurityPolicies.Basic256Sha256, "Basic256Sha256");
        NodeId temperature = UaOperations.Resolve(session, "Temperature");
        NodeId setpoint = UaOperations.Resolve(session, "Setpoint");
        Check(temperature.NamespaceIndex > 0, "Resolve namespace URI instead of hardcoded ns=2");
        var browser = new Browser(session) { BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences, IncludeSubtypes = true };
        var children = await browser.BrowseAsync(new NodeId("Demo", temperature.NamespaceIndex), ct);
        Check(children.Count == 3, "Browse Demo contains three variables");
        DataValue value = await session.ReadValueAsync(temperature, ct);
        Check(UaOperations.RequireGoodDouble(value) == 23.5, "Read finite Double with Good quality");
        Check(value.SourceTimestamp != default && value.ServerTimestamp != default, "Read source/server timestamps");
        DataValue ready = await session.ReadValueAsync(UaOperations.Resolve(session, "Ready"), ct);
        Check(StatusCode.IsGood(ready.StatusCode) && ready.WrappedValue.TryGetValue(out bool isReady) && isReady, "Read typed Boolean");

        // Await initial notification before writing, so setup races cannot pass the change check.
        using var subscriptionBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        subscriptionBudget.CancelAfter(TimeSpan.FromSeconds(10));
        await using (var updates = session.DefaultStreaming.SubscribeDataChangesAsync(setpoint,
            new MonitoredItemOptions { SamplingInterval = TimeSpan.FromMilliseconds(100), QueueSize = 4,
                DiscardOldest = true, TimestampsToReturn = TimestampsToReturn.Both },
            subscriptionBudget.Token).GetAsyncEnumerator(subscriptionBudget.Token))
        {
            Check(await updates.MoveNextAsync(), "Subscription initial notification");
            Check(UaOperations.RequireGoodDouble(updates.Current.Value) == 25.0, "Subscription initial value");
            Check(StatusCode.IsGood(await UaOperations.WriteAsync(session, setpoint, new Variant(30.0), ct)), "Write Double returns Good");
            bool changed = false;
            while (await updates.MoveNextAsync())
            {
                if (UaOperations.RequireGoodDouble(updates.Current.Value) == 30.0) { changed = true; break; }
            }
            Check(changed, "Subscription receives written value");
        }
        Check(UaOperations.RequireGoodDouble(await session.ReadValueAsync(setpoint, ct)) == 30.0, "Readback matches");
        using (var stopSubscription = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            await using var updates = session.DefaultStreaming.SubscribeDataChangesAsync(setpoint,
                ct: stopSubscription.Token).GetAsyncEnumerator(stopSubscription.Token);
            Check(await updates.MoveNextAsync(), "Second subscription receives initial value");
            stopSubscription.Cancel();
            try { await updates.MoveNextAsync(); throw new Exception("Canceled stream did not stop"); }
            catch (OperationCanceledException) when (stopSubscription.IsCancellationRequested)
            { Check(true, "Canceled subscription awaited before disposal"); }
        }
        Check(await UaOperations.WriteAsync(session, temperature, new Variant(80.0), ct) == StatusCodes.BadNotWritable, "Readonly variable refuses write");
        Check(await UaOperations.WriteAsync(session, setpoint, new Variant("30"), ct) == StatusCodes.BadTypeMismatch, "String is not Double");
        Check(await UaOperations.WriteAsync(session, setpoint, new Variant(101.0), ct) == StatusCodes.BadOutOfRange, "Server rejects out-of-range target");
        Check(await UaOperations.WriteAsync(session, setpoint, new Variant(double.NaN), ct) == StatusCodes.BadOutOfRange, "Server rejects NaN");
        Check(UaOperations.RequireGoodDouble(await session.ReadValueAsync(setpoint, ct)) == 30.0, "Rejected writes leave value unchanged");
        try { await session.ReadValueAsync(UaOperations.Resolve(session, "Missing"), ct); throw new Exception("Unknown node accepted"); }
        catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown) { Check(true, "Unknown node raises BadNodeIdUnknown"); }
        try { UaOperations.RequireGoodDouble(new DataValue(new Variant(30.0), StatusCodes.Uncertain)); throw new Exception("Uncertain accepted"); }
        catch (InvalidDataException) { Check(true, "Business layer refuses Uncertain"); }
        await session.CloseAsync(ct);
        Check(!session.Connected, "Explicit session close");
    }
    finally
    {
        await trustedClient.StopAsync(CancellationToken.None);
        await trustedServer.StopAsync(CancellationToken.None);
    }
    Check(!IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port), "Server listener closed after shutdown");
    Console.WriteLine($"Completed {checks} checks. No physical device or Windows certificate store touched.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
