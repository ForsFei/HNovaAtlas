using System.Diagnostics;
using McpXLib;
using McpXLib.Enums;
using McpXLib.Exceptions;

namespace IndustrialHandshake;

// One publisher, one operation at a time. This class does NOT arbitrate multiple PCs.
internal sealed class HandshakeClient(LoopbackController controller)
{
    private Task<T> SessionAsync<T>(Func<McpX, Task<T>> work, CancellationToken stop) => Task.Run(async () =>
    {
        stop.ThrowIfCancellationRequested();
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Little-endian baseline only.");
        using var plc = new McpX(controller.Endpoint.Address.ToString(), controller.Endpoint.Port,
            password: null, isAscii: false, isUdp: false, requestFrame: RequestFrame.E3,
            timeoutMilliseconds: 800, processorSeries: ProcessorSeries.Q);
        stop.ThrowIfCancellationRequested();
        return await work(plc);
    });

    public static async Task<Snapshot> ReadStatusAsync(McpX plc)
        => Snapshot.Decode(await plc.BatchReadAsync<ushort>(Prefix.D, "240", 18));

    public Task<Snapshot> InspectAsync(CancellationToken stop = default) => SessionAsync(ReadStatusAsync, stop);

    public static Ticket CreateTicket(Snapshot status, short target)
    {
        if (target is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(target));
        if (status.State != ActionState.Idle || status.Control != 0 || status.ControlAck != 0)
            throw new InvalidOperationException("Resolve/release previous mailbox before allocating another request.");
        ushort next = checked((ushort)(status.HighWater + 1)); // Never wrap and reuse identity.
        return new(status.BootId, next, 1, target);
    }

    public Task<Observation> SubmitAsync(Ticket ticket, CancellationToken stop = default) => ControlAsync(ticket, 1, stop);
    public Task<Observation> ReleaseAsync(Ticket ticket, CancellationToken stop = default) => ControlAsync(ticket, 2, stop);

    private async Task<Observation> ControlAsync(Ticket ticket, ushort control, CancellationToken stop)
    {
        bool controlAttempted = false;
        Snapshot? last = null;
        try
        {
            return await SessionAsync(async plc =>
            {
                last = await ReadStatusAsync(plc);
                if (last.BootId != ticket.BootId) return new Observation(Resolution.EpochChanged, last, "Old controller boot; do not reissue.");
                if (last.Control != 0 || last.ControlAck != 0)
                    return new Observation(Resolution.Unknown, last, "Previous control phase is unresolved. Query before resetting it.");
                stop.ThrowIfCancellationRequested();
                await plc.BatchWriteAsync<ushort>(Prefix.D, "200", ticket.Encode());
                stop.ThrowIfCancellationRequested();
                controlAttempted = true; // Set BEFORE awaiting I/O: failure does not prove the word was not written.
                await plc.WriteAsync<ushort>(Prefix.D, "206", control);
                var budget = Stopwatch.StartNew();
                do
                {
                    stop.ThrowIfCancellationRequested();
                    last = await ReadStatusAsync(plc);
                    if (last.BootId != ticket.BootId) return new Observation(Resolution.EpochChanged, last, "Controller restarted during operation.");
                    if (last.ControlAck == control && last.ReplyId == ticket.RequestId)
                    {
                        Snapshot evidence = last;
                        await plc.WriteAsync<ushort>(Prefix.D, "206", 0);
                        last = await ReadStatusAsync(plc);
                        if (last.BootId != ticket.BootId) return new Observation(Resolution.EpochChanged, last, "Controller restarted during reset.");
                        if (last.Control != 0 || last.ControlAck != 0)
                            return new Observation(Resolution.Unknown, last, "Reset not confirmed; preserve evidence and reconcile.");
                        return new Observation(Resolution.Known, evidence, "Mailbox reply observed; inspect Reply and action State separately.");
                    }
                    await Task.Delay(10, stop);
                } while (budget.Elapsed < TimeSpan.FromSeconds(2));
                return new Observation(Resolution.Unknown, last, "Control acknowledgement not observed; query, never auto-submit.");
            }, stop);
        }
        catch (Exception error) when (error is IOException or TimeoutException or System.Net.Sockets.SocketException or
                                             RecivePacketException or McProtocolException or InvalidDataException or OperationCanceledException)
        {
            return new(controlAttempted ? Resolution.Unknown : Resolution.NotSubmitted, last,
                $"{error.GetType().Name}: {error.Message}. Caller stop does not stop the device.");
        }
    }

    public async Task<Observation> QueryAsync(Ticket ticket, CancellationToken stop = default)
    {
        try
        {
            Snapshot status = await InspectAsync(stop);
            if (status.BootId != ticket.BootId) return new(Resolution.EpochChanged, status, "Deduplication history from prior boot is unavailable.");
            if (status.Current.RequestId != ticket.RequestId || status.State == ActionState.Idle)
                return new(Resolution.NotRetained, status, "No matching retained result; not proof of non-execution.");
            return status.Current == ticket
                ? new(Resolution.Known, status, "Matching key AND payload retained.")
                : new(Resolution.Conflict, status, "Same request identity has different parameters.");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return new(Resolution.WaitCanceled, null, "Local query stopped."); }
        catch (Exception error) when (error is IOException or TimeoutException or System.Net.Sockets.SocketException or RecivePacketException or McProtocolException or InvalidDataException)
        { return new(Resolution.Unknown, null, error.GetType().Name + ": " + error.Message); }
    }

    // Explicit query-confirmed RESET of a held control; never replay control=1/2.
    public Task<bool> ResetObservedControlAsync(Ticket ticket, CancellationToken stop = default) => SessionAsync(async plc =>
    {
        Snapshot status = await ReadStatusAsync(plc);
        if (status.BootId != ticket.BootId || status.Control == 0 || status.ControlAck != status.Control ||
            status.ReplyId != ticket.RequestId) return false;
        stop.ThrowIfCancellationRequested();
        await plc.WriteAsync<ushort>(Prefix.D, "206", 0);
        status = await ReadStatusAsync(plc);
        return status.BootId == ticket.BootId && status.Control == 0 && status.ControlAck == 0;
    }, stop);

    public async Task<Observation> WaitAsync(Ticket ticket, TimeSpan timeout, CancellationToken stop = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var elapsed = Stopwatch.StartNew();
        Snapshot? last = null;
        while (elapsed.Elapsed < timeout)
        {
            Observation value = await QueryAsync(ticket, stop);
            if (value.Resolution != Resolution.Known || value.Snapshot!.Terminal) return value;
            last = value.Snapshot;
            try { await Task.Delay(15, stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            { return new(Resolution.WaitCanceled, last, "Only local observation stopped; device may still run."); }
        }
        return new(Resolution.WaitTimeout, last, "Business wait expired; no stop/reset/replay was sent.");
    }
}
