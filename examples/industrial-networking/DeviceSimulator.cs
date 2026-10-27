using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IndustrialNetworking;

public enum DeviceScenario { Normal, Split, Slow, Close, Partial, Malformed, WrongId, Drop, StaleThenValid, WrongSourceThenValid, Empty, Oversize }

public static class DeviceSimulator
{
    public static async Task RunTcpAsync(int port, DeviceScenario scenario, Action<IPEndPoint> ready,
        CancellationToken stop, Action<string>? log = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            ready((IPEndPoint)listener.LocalEndpoint);
            while (!stop.IsCancellationRequested)
            {
                using TcpClient peer = await listener.AcceptTcpClientAsync(stop);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(stop);
                request.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    using NetworkStream stream = peer.GetStream();
                    string id = WireProtocol.ParseRequest(await NetworkQueries.ReadFrameAsync(stream, request.Token));
                    log?.Invoke($"TCP RX request={id}");
                    if (scenario == DeviceScenario.Close) continue;
                    if (scenario == DeviceScenario.Slow) await Task.Delay(2000, request.Token);
                    byte[] response = MakeResponse(id, scenario);
                    if (scenario == DeviceScenario.Partial) response = Encoding.ASCII.GetBytes("TEMP ");
                    if (scenario == DeviceScenario.Split)
                    {
                        int cut = response.Length - 1; // Put CR and LF in separate writes.
                        await stream.WriteAsync(response.AsMemory(0, cut), request.Token);
                        await Task.Delay(25, request.Token);
                        await stream.WriteAsync(response.AsMemory(cut), request.Token);
                    }
                    else await stream.WriteAsync(response, request.Token);
                }
                catch (Exception error) when (error is IOException or SocketException or FormatException or OperationCanceledException)
                {
                    if (stop.IsCancellationRequested) break;
                    log?.Invoke($"TCP session: {error.GetType().Name}: {error.Message}");
                }
                // Accepted connection is disposed even after malformed data or timeout.
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { listener.Stop(); }
    }

    public static async Task RunUdpAsync(int port, DeviceScenario scenario, Action<IPEndPoint> ready,
        CancellationToken stop, Action<string>? log = null)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        ready((IPEndPoint)udp.Client.LocalEndPoint!);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult received = await udp.ReceiveAsync(stop);
                try
                {
                    string id = WireProtocol.ParseRequest(WireProtocol.DecodeDatagram(received.Buffer));
                    log?.Invoke($"UDP RX request={id}, source={received.RemoteEndPoint}");
                    if (scenario == DeviceScenario.Drop) continue;
                    if (scenario == DeviceScenario.Slow) await Task.Delay(2000, stop);
                    if (scenario == DeviceScenario.StaleThenValid)
                    {
                        await udp.SendAsync(WireProtocol.Response(WireProtocol.NewId()), received.RemoteEndPoint, stop);
                        await Task.Delay(25, stop);
                    }
                    if (scenario == DeviceScenario.WrongSourceThenValid)
                    {
                        using var other = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                        await other.SendAsync(WireProtocol.Response(id), received.RemoteEndPoint, stop);
                        await Task.Delay(25, stop);
                    }
                    byte[] response = scenario switch
                    {
                        DeviceScenario.Empty => [],
                        DeviceScenario.Oversize => new byte[512],
                        _ => MakeResponse(id, scenario),
                    };
                    await udp.SendAsync(response, received.RemoteEndPoint, stop);
                }
                catch (FormatException error) { log?.Invoke($"UDP invalid: {error.Message}"); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private static byte[] MakeResponse(string id, DeviceScenario scenario) => scenario switch
    {
        DeviceScenario.Malformed => WireProtocol.Encode($"TEMP {id} abc"),
        DeviceScenario.WrongId => WireProtocol.Response(WireProtocol.NewId()),
        _ => WireProtocol.Response(id),
    };
}
