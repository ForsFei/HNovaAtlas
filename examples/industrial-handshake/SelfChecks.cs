using System.Net;
using System.Net.Sockets;

namespace IndustrialHandshake;

internal static class SelfChecks
{
    private static int _count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, label); return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + label);
    }
    private static Reply Send(Mailbox model, Ticket ticket, ushort control = 1)
    {
        model.SetControl(0); model.Stage(0, ticket.Encode()); model.SetControl(control);
        Reply reply = model.Read().Reply;
        model.SetControl(0);
        return reply;
    }

    public static async Task RunAsync()
    {
        var model = new Mailbox();
        Ticket first = new(1, 1, 1, 300);
        Check(Ticket.Decode(first.Encode()) == first, "Request words round trip");
        model.Stage(0, first.Encode()[..3]);
        model.Advance();
        Check(model.Read().State == ActionState.Idle && model.Read().ExecutionCount == 0, "Partial staging never starts action");
        model.Stage(3, first.Encode()[3..]); model.SetControl(1);
        Check(model.Read().ControlAck == 1 && model.Read().State == ActionState.Accepted, "Commit latches complete immutable payload");
        Check(model.Read().AcceptedId == 1 && model.Read().CompletedId == 0 && model.Read().ExecutionCount == 0, "Accepted is not running or completed");
        model.SetControl(1); model.SetControl(1);
        Check(model.Read().HighWater == 1 && model.Read().ExecutionCount == 0, "Held control level does not retrigger");
        Throws<InvalidOperationException>(() => model.Stage(0, first.Encode()), "Staging frozen while control is held");
        model.SetControl(0); model.Advance();
        Check(model.Read().State == ActionState.Running && model.Read().ExecutionCount == 1, "One accepted action starts once");
        Check(Send(model, first) == Reply.Duplicate && model.Read().ExecutionCount == 1, "Same key and payload deduplicated while running");
        Check(Send(model, first with { Target = 350 }) == Reply.Conflict && model.Read().Current.Target == 300, "Same key with different payload conflicts");
        Check(Send(model, first with { RequestId = 2 }) == Reply.Busy && model.Read().Current == first, "New request cannot overwrite active action");
        Check(Send(model, first, 2) == Reply.NotTerminal && model.Read().State == ActionState.Running, "Early result release refused");
        model.Advance();
        Check(model.Read().State == ActionState.Done && model.Read().CompletedId == 1, "Done retains matching completed identity");
        model.Advance(); model.Advance();
        Check(model.Read().State == ActionState.Done, "Terminal result remains until explicit release");
        Check(Send(model, first) == Reply.Duplicate && model.Read().ExecutionCount == 1, "Duplicate after completion not executed again");
        Check(Send(model, first with { RequestId = 2 }, 2) == Reply.Stale && model.Read().State == ActionState.Done, "Wrong release cannot clear result");
        Check(Send(model, first, 2) == Reply.Released && model.Read().State == ActionState.Idle, "Matching terminal result explicitly released");
        Check(model.Read().HighWater == 1 && Send(model, first) == Reply.Stale, "Released request cannot replay within same boot");
        Check(Send(model, first with { RequestId = 2, Version = 9 }) == Reply.Recorded && model.Read().State == ActionState.Rejected &&
              model.Read().Result == ResultCode.Unsupported && model.Read().AcceptedId == 0, "Mailbox receipt is distinct from business rejection");
        var rejected = model.Read().Current;
        Send(model, rejected, 2);
        model.Interlock = false;
        Ticket third = first with { RequestId = 3 };
        Send(model, third);
        Check(model.Read().State == ActionState.Rejected && model.Read().Result == ResultCode.Interlock, "Interlock rejects before acceptance");
        Send(model, third, 2); model.Interlock = true;
        Ticket fourth = first with { RequestId = 4 };
        Send(model, fourth); model.Interlock = false; model.Advance();
        Check(model.Read().State == ActionState.Faulted && model.Read().ExecutionCount == 1, "Lost interlock before start prevents execution");
        Send(model, fourth, 2); model.Interlock = true; model.FailDuringRun = true;
        Ticket fifth = first with { RequestId = 5 };
        Send(model, fifth); model.Advance(); model.Advance();
        Check(model.Read().State == ActionState.Faulted && model.Read().CompletedId == 0 && model.Read().Result == ResultCode.ExecutionFault,
            "Execution fault does not become Done");
        Snapshot good = model.Read();
        Check(Snapshot.Decode(good.Encode()) == good, "Status words round trip with identity");
        ushort[] torn = good.Encode(); torn[17] ^= 2;
        Throws<InvalidDataException>(() => Snapshot.Decode(torn), "Mismatched publication guards rejected");
        ushort[] odd = good.Encode(); odd[0] = odd[17] = 1;
        Throws<InvalidDataException>(() => Snapshot.Decode(odd), "In-progress odd revision rejected");
        ushort[] wrongDone = good.Encode(); wrongDone[5] = (ushort)ActionState.Done; wrongDone[8] = 123;
        Throws<InvalidDataException>(() => Snapshot.Decode(wrongDone), "Unmatched CompletedId never means completion");
        model.Reboot();
        Check(model.Read().BootId == 2 && Send(model, first) == Reply.EpochMismatch, "Old boot identity refused after reset");
        model.SetControl(0);
        Snapshot exhausted = model.Read() with { HighWater = ushort.MaxValue };
        Throws<OverflowException>(() => HandshakeClient.CreateTicket(exhausted, 300), "Request identity never silently wraps");

        await using (var device = new LoopbackController())
        {
            Check(IPAddress.IsLoopback(device.Endpoint.Address), "Real MC server is loopback only");
            var client = new HandshakeClient(device);
            Ticket ticket = HandshakeClient.CreateTicket(await client.InspectAsync(), 300);
            Observation sent = await client.SubmitAsync(ticket);
            Check(sent.Resolution == Resolution.Known && sent.Snapshot!.Reply == Reply.Recorded, "Real SDK stages, commits and observes mailbox reply");
            Check((await client.WaitAsync(ticket, TimeSpan.FromSeconds(3))) is { Resolution: Resolution.Known, Snapshot.State: ActionState.Done }, "Poll reaches retained matching Done");
            ushort executions = device.Model.Read().ExecutionCount;
            Observation duplicate = await client.SubmitAsync(ticket);
            Check(duplicate.Snapshot!.Reply == Reply.Duplicate && device.Model.Read().ExecutionCount == executions, "Wire duplicate not executed twice");
            Observation conflict = await client.SubmitAsync(ticket with { Target = 301 });
            Check(conflict.Snapshot!.Reply == Reply.Conflict, "Wire conflicting payload refused");
            Observation busy = await client.SubmitAsync(ticket with { RequestId = 2 });
            Check(busy.Snapshot!.Reply == Reply.Busy, "Held terminal result blocks new request");
            Check((await client.ReleaseAsync(ticket)).Snapshot!.Reply == Reply.Released, "Explicit result release over MC");
            Check((await client.QueryAsync(ticket)).Resolution == Resolution.NotRetained, "Absent retained result is not non-execution proof");
            Check((await client.SubmitAsync(ticket)).Snapshot!.Reply == Reply.Stale && device.Model.Read().ExecutionCount == 1, "Stale request refused after release over wire");
        }
        await using (var device = new LoopbackController { DropNextSubmitReply = true })
        {
            var client = new HandshakeClient(device);
            Ticket ticket = HandshakeClient.CreateTicket(await client.InspectAsync(), 400);
            Observation sent = await client.SubmitAsync(ticket);
            Check(sent.Resolution == Resolution.Unknown, "Lost commit response preserved as Unknown");
            int writes = device.SubmitWrites;
            Observation query = await client.WaitAsync(ticket, TimeSpan.FromSeconds(3));
            Check(query is { Resolution: Resolution.Known, Snapshot.State: ActionState.Done } && device.Model.Read().ExecutionCount == 1,
                "Reconnect queries same identity and finds completed action");
            Check(device.SubmitWrites == writes && writes == 1 && device.Connections >= 3, "Query reconnects without replaying submit");
            Check(await client.ResetObservedControlAsync(ticket), "Query-confirmed explicit zero reset of held control");
            Check((await client.ReleaseAsync(ticket)).Snapshot!.Reply == Reply.Released, "Reconciled result can be explicitly released");
        }
        await using (var device = new LoopbackController())
        {
            device.Model.HoldProgress = true;
            var client = new HandshakeClient(device);
            Ticket ticket = HandshakeClient.CreateTicket(await client.InspectAsync(), 450);
            await client.SubmitAsync(ticket);
            Observation timeout = await client.WaitAsync(ticket, TimeSpan.FromMilliseconds(60));
            Check(timeout.Resolution == Resolution.WaitTimeout && device.Model.Read().State == ActionState.Accepted, "Business wait timeout does not cancel accepted action");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Check((await client.WaitAsync(ticket, TimeSpan.FromSeconds(1), canceled.Token)).Resolution == Resolution.WaitCanceled,
                "Cancel only stops local waiting");
            Check(device.SubmitWrites == 1 && device.ReleaseWrites == 0, "Timeout/cancel send no stop/release/replay");
            device.Model.HoldProgress = false;
            Check((await client.WaitAsync(ticket, TimeSpan.FromSeconds(3))).Snapshot!.State == ActionState.Done, "Device may complete after caller stops waiting");
        }
        await using (var device = new LoopbackController())
        {
            device.Model.Interlock = false;
            var client = new HandshakeClient(device);
            Ticket ticket = HandshakeClient.CreateTicket(await client.InspectAsync(), 300);
            await client.SubmitAsync(ticket);
            Check((await client.WaitAsync(ticket, TimeSpan.FromSeconds(1))).Snapshot!.State == ActionState.Rejected && device.Model.Read().ExecutionCount == 0,
                "Wire business rejection without execution");
        }
        await using (var device = new LoopbackController())
        {
            device.Model.FailDuringRun = true;
            var client = new HandshakeClient(device);
            Ticket ticket = HandshakeClient.CreateTicket(await client.InspectAsync(), 300);
            await client.SubmitAsync(ticket);
            Check((await client.WaitAsync(ticket, TimeSpan.FromSeconds(3))).Snapshot!.State == ActionState.Faulted, "Wire execution fault retained");
        }
        await using (var device = new LoopbackController())
        {
            device.Model.HoldProgress = true;
            var client = new HandshakeClient(device);
            Ticket ticket = HandshakeClient.CreateTicket(await client.InspectAsync(), 500);
            await client.SubmitAsync(ticket); device.Model.Reboot();
            Check((await client.QueryAsync(ticket)).Resolution == Resolution.EpochChanged, "Reboot cannot be interpreted as old action not executed");
            Check((await client.SubmitAsync(ticket)).Resolution == Resolution.EpochChanged && device.SubmitWrites == 1, "Client does not reissue old action after reboot");
        }
        await using (var device = new LoopbackController())
        {
            var client = new HandshakeClient(device);
            Ticket ticket = HandshakeClient.CreateTicket(await client.InspectAsync(), 300);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Check((await client.SubmitAsync(ticket, canceled.Token)).Resolution == Resolution.NotSubmitted && device.SubmitWrites == 0, "Canceled before commit is NotSubmitted");
            device.TornNextStatus = true;
            Check((await client.QueryAsync(ticket)).Resolution == Resolution.Unknown, "Torn status rejected rather than published as valid");
        }
        await using (var device = new LoopbackController())
        {
            var client = new HandshakeClient(device);
            Ticket ticket = HandshakeClient.CreateTicket(await client.InspectAsync(), 300);
            await client.SubmitAsync(ticket); await client.WaitAsync(ticket, TimeSpan.FromSeconds(3));
            device.DropNextReleaseReply = true;
            Check((await client.ReleaseAsync(ticket)).Resolution == Resolution.Unknown, "Lost release reply stays uncertain");
            Check((await client.QueryAsync(ticket)).Resolution == Resolution.NotRetained && device.Model.Read().ExecutionCount == 1,
                "Released result absence never triggers a new action");
            Check(await client.ResetObservedControlAsync(ticket), "Held release control can be query-confirmed and reset");
        }
        var shutdown = new LoopbackController(); var endpoint = shutdown.Endpoint;
        await shutdown.DisposeAsync();
        using var socket = new TcpClient();
        try { await socket.ConnectAsync(endpoint.Address, endpoint.Port); throw new Exception("Listener leaked"); }
        catch (SocketException) { Check(true, "Both network service and simulated scan stopped; port released"); }
        Console.WriteLine($"Completed {_count} checks. Simulated action only; no physical actuator or safety function.");
    }
}
