using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using McpXLib.Enums;
using McpXLib.Exceptions;

namespace IndustrialMc;

internal static class SelfChecks
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine($"PASS {++_checks}: {name}");
    }

    private static async Task ThrowsAsync<T>(Func<Task> work, string name) where T : Exception
    {
        try { await work(); }
        catch (T) { Check(true, name); return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + name);
    }

    public static async Task RunAsync()
    {
        byte[] readD100 = Convert.FromHexString("500000FFFF03000C00020001040000640000A80200");
        Check(McFrames.RequestLength(readD100.AsSpan(0, 9)) == 12, "3E golden request length");
        Check(McFrames.U16(readD100.AsSpan(9)) == 2, "Monitoring timer 2 = 500 ms");
        Check(Convert.ToHexString(McFrames.PackBits([true, false, true, false, true])) == "101010", "Bit high nibble first, odd padding");
        Check(McFrames.UnpackBits([0x10, 0x10, 0x10], 5).SequenceEqual([true, false, true, false, true]), "Unpack five bits");
        await ThrowsAsync<InvalidDataException>(() => { McFrames.UnpackBits([0x20], 1); return Task.CompletedTask; }, "Reject invalid nibble");
        await ThrowsAsync<InvalidDataException>(() => { McFrames.UnpackBits([0x11], 1); return Task.CompletedTask; }, "Reject nonzero padding");
        byte[] oversized = (byte[])readD100.Clone(); oversized[7] = 255; oversized[8] = 255;
        await ThrowsAsync<InvalidDataException>(() => { McFrames.RequestLength(oversized.AsSpan(0, 9)); return Task.CompletedTask; }, "Bounded request allocation");
        Check(Convert.ToHexString(McFrames.Response(0, [0x85, 0xFF])) == "D00000FFFF03000400000085FF", "3E golden response and little-endian signed value");

        await using (var device = new LoopbackMcDevice())
        {
            Check(IPAddress.IsLoopback(device.Endpoint.Address), "Listener bound to loopback / OS allocated port");
            await McExamples.RunAsync(device, async (plc, stop) =>
            {
                Check(plc.RequestFrame == RequestFrame.E3 && !plc.IsAscii && plc.ProcessorSeries == ProcessorSeries.Q, "Pinned binary 3E Q format");
                short[] two = await McExamples.ReadExactAsync<short>(plc, Prefix.D, "100", 2);
                Check(two[0] == -123, "Read signed Int16 D100");
                Check(Convert.ToHexString(device.LastRequest!) == Convert.ToHexString(readD100), "Actual SDK request matches independent golden frame");
                Check(await plc.ReadAsync<ushort>(Prefix.D, "105") == 65535, "Unsigned D105");
                Check(await plc.ReadAsync<uint>(Prefix.D, "101") == 0x12345678, "UInt32 spans two D words, low word first");
                Check(await plc.ReadAsync<float>(Prefix.D, "103") == 23.5f, "IEEE Single spans D103-D104");
                Check(await plc.ReadAsync<ushort>(Prefix.W, "10") == 1111, "W10 interpreted hexadecimal 0x10");
                Check(await plc.ReadAsync<ushort>(Prefix.W, "16") == 2222, "W16 differs from W10");
                Check(await plc.ReadAsync<bool>(Prefix.X, "10"), "X10 bit read with hexadecimal address");
                bool[] raw = await plc.BatchReadAsync<bool>(Prefix.M, "100", 5);
                Check(raw.Length == 5, "SDK removes final odd-bit padding");
                Check((await McExamples.ReadExactAsync<bool>(plc, Prefix.M, "100", 5)).SequenceEqual([true, false, true, false, true]), "Adapter returns exactly five contracted bits");
                await McExamples.WriteTargetAsync(plc, 300, stop);
                Check(await plc.ReadAsync<short>(Prefix.D, "110") == 300, "Write Int16 target then readback");
                int before = device.Writes;
                await ThrowsAsync<ArgumentOutOfRangeException>(() => McExamples.WriteTargetAsync(plc, 1001), "Client rejects out-of-range target before send");
                Check(device.Writes == before, "No invalid target write sent");
                try { await plc.WriteAsync<short>(Prefix.D, "110", 1001); throw new Exception("Expected teaching refusal"); }
                catch (McProtocolException ex) { Check(ex.ErrorCode == 0xC059 && device.InspectD(110) == 300, "Server refusal preserves target and exposes end code"); }
                await plc.BatchWriteAsync<int>(Prefix.D, "120", [123456, -789]);
                Check((await McExamples.ReadExactAsync<int>(plc, Prefix.D, "120", 2)).SequenceEqual([123456, -789]), "Two Int32 values occupy D120-D123");
                Check(McFrames.U16(device.LastRequest!.AsSpan(19)) == 4, "Batch count is four wire words, not two typed values");
                await plc.BatchWriteAsync<bool>(Prefix.M, "100", [false, true, false, true, false]);
                Check((await McExamples.ReadExactAsync<bool>(plc, Prefix.M, "100", 5)).SequenceEqual([false, true, false, true, false]), "Five-bit write/readback");
                int requests = device.Requests;
                await ThrowsAsync<DeviceAddressException>(async () => { await plc.ReadAsync<short>(Prefix.D, "1A"); }, "Decimal D address rejects letters");
                Check(device.Requests == requests, "Invalid address not sent");
                return true;
            });
        }

        await using (var device = new LoopbackMcDevice(McFault.SplitReply))
            await McExamples.RunAsync(device, async (plc, _) => { Check(await plc.ReadAsync<short>(Prefix.D, "100") == -123, "Fragmented TCP response reassembled"); return true; });
        await using (var device = new LoopbackMcDevice(McFault.Reject))
            await McExamples.RunAsync(device, async (plc, _) => { await ThrowsAsync<McProtocolException>(async () => { await plc.ReadAsync<short>(Prefix.D, "100"); }, "Nonzero end code is not a successful read"); return true; });
        await using (var device = new LoopbackMcDevice(McFault.BadHeader))
            await McExamples.RunAsync(device, async (plc, _) => { await ThrowsAsync<RecivePacketException>(async () => { await plc.ReadAsync<short>(Prefix.D, "100"); }, "Malformed response subheader rejected"); return true; });
        await using (var device = new LoopbackMcDevice(McFault.ShortRead))
            await McExamples.RunAsync(device, async (plc, _) => { await ThrowsAsync<InvalidDataException>(async () => { await McExamples.ReadExactAsync<short>(plc, Prefix.D, "100", 2); }, "Adapter rejects short successful payload"); return true; });
        await using (var device = new LoopbackMcDevice(McFault.Silent))
            await McExamples.RunAsync(device, async (plc, _) =>
            {
                var elapsed = Stopwatch.StartNew();
                await ThrowsAsync<TimeoutException>(async () => { await plc.ReadAsync<short>(Prefix.D, "100"); }, "Silent response hits per-request deadline");
                Check(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Deadline completes within test watchdog margin");
                await ThrowsAsync<IOException>(async () => { await plc.ReadAsync<short>(Prefix.D, "100"); }, "Broken TCP instance cannot be reused");
                Check(device.Requests == 1, "No retry after timeout");
                return true;
            });
        await using (var device = new LoopbackMcDevice(McFault.Close))
            await McExamples.RunAsync(device, async (plc, _) => { await ThrowsAsync<IOException>(async () => { await plc.ReadAsync<short>(Prefix.D, "100"); }, "EOF reported"); return true; });
        await using (var device = new LoopbackMcDevice(McFault.ApplyThenDrop))
        {
            await ThrowsAsync<IOException>(() => McExamples.RunAsync(device, async (plc, stop) => { await McExamples.WriteTargetAsync(plc, 350, stop); return true; }), "Write acknowledgement lost: caller sees failure");
            Check(device.InspectD(110) == 350 && device.Writes == 1, "Write already applied, result unknown to client, no replay");
        }
        await using (var device = new LoopbackMcDevice())
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => McExamples.RunAsync(device, (_, _) => Task.FromResult(true), canceled.Token), "Cancellation before construction");
            Check(device.Requests == 0, "No command after early stop");
        }
        await using (var device = new LoopbackMcDevice(McFault.Silent))
        using (var stop = new CancellationTokenSource())
        {
            Task work = McExamples.RunAsync(device, async (plc, token) =>
            {
                await ThrowsAsync<TimeoutException>(async () => { await plc.ReadAsync<short>(Prefix.D, "100"); }, "In-flight operation awaited to library deadline");
                token.ThrowIfCancellationRequested();
                return true;
            }, stop.Token);
            await device.RequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => work, "Stop observed after in-flight await; no pretend immediate cancellation");
            Check(device.Requests == 1, "Stop does not enqueue another request");
        }
        var closed = new LoopbackMcDevice();
        await closed.DisposeAsync();
        using var probe = new TcpClient();
        await ThrowsAsync<SocketException>(async () => { await probe.ConnectAsync(closed.Endpoint.Address, closed.Endpoint.Port); }, "Server port released on awaited shutdown");
        Console.WriteLine($"Completed {_checks} checks. No PLC, GX project, physical I/O or remote CPU command touched.");
    }
}
