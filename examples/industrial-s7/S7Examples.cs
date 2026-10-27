using System.Net;
using S7.Net;

namespace IndustrialS7;

public sealed record DbSnapshot(bool Ready, byte Mode, decimal Temperature, int Count,
    float Measurement, short TargetRaw, bool RequestFlag, bool OtherFlag, string Name);

public static class S7Examples
{
    public static Plc CreateDemoPlc(int loopbackPort) => new(CpuType.S71200, "127.0.0.1", loopbackPort, 0, 1)
    { ReadTimeout = 1000, WriteTimeout = 1000 };

    public static DbSnapshot Decode(byte[] db)
    {
        if (db.Length < 26) throw new FormatException("DB1 教学快照至少需要26字节。");
        short rawTemperature = S7.Net.Types.Int.FromByteArray(db.AsSpan(2, 2).ToArray());
        int count = S7.Net.Types.DInt.FromByteArray(db.AsSpan(4, 4).ToArray());
        float real = S7.Net.Types.Real.FromByteArray(db.AsSpan(8, 4).ToArray());
        short target = S7.Net.Types.Int.FromByteArray(db.AsSpan(12, 2).ToArray());
        if (!float.IsFinite(real)) throw new FormatException("测量值不接受 NaN/Infinity。");
        if (db[16] != 8 || db[17] > 8 || db.AsSpan(18, db[17]).ToArray().Any(b => b is < 0x20 or > 0x7E))
            throw new FormatException("期待 STRING[8] 的可打印 ASCII 教学数据。");
        string name = S7.Net.Types.S7String.FromByteArray(db.AsSpan(16, 10).ToArray());
        return new((db[0] & 1) != 0, db[1], rawTemperature / 10m, count, real, target,
            (db[14] & 1) != 0, (db[14] & 2) != 0, name);
    }

    public static async Task WriteDemoTargetAsync(Plc plc, short raw, CancellationToken stop)
    {
        if (!IPAddress.TryParse(plc.IP, out IPAddress? address) || !IPAddress.IsLoopback(address))
            throw new InvalidOperationException("此教学写入仅允许本机回环对象。");
        if (raw is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(raw));
        // Explicit Int16 runtime type: passing an untyped int could write four bytes!
        await plc.WriteAsync("DB1.DBW12", raw, stop);
    }

    public static async Task<DbSnapshot> RunDemoAsync(CancellationToken stop)
    {
        using var device = new Snap7TestDevice();
        using Plc plc = CreateDemoPlc(device.Port);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stop);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await plc.OpenAsync(budget.Token);
            Console.WriteLine($"已建立本机 S7 会话：{plc.IP}:{plc.Port}，协商 PDU={plc.MaxPDUSize}。");
            DbSnapshot before = Decode(await plc.ReadBytesAsync(DataType.DataBlock, 1, 0, 26, budget.Token));
            Console.WriteLine($"初值：Temp={before.Temperature:0.0}，Real={before.Measurement:0.0}，TargetRaw={before.TargetRaw}，Name={before.Name}。");
            await WriteDemoTargetAsync(plc, (short)300, budget.Token);
            await plc.WriteAsync("DB1.DBX14.0", true, budget.Token);
            DbSnapshot after = Decode(await plc.ReadBytesAsync(DataType.DataBlock, 1, 0, 26, budget.Token));
            Console.WriteLine($"写后：TargetRaw={after.TargetRaw}，Request={after.RequestFlag}，其他标志={after.OtherFlag}。");
            return after;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested && budget.IsCancellationRequested)
        { throw new TimeoutException("S7 教学操作期限到达；放弃旧会话，不自动重发。"); }
        finally { plc.Close(); }
    }
}
