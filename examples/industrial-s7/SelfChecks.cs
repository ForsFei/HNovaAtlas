using S7.Net;
using S7.Net.Types;
using System.Net;
using System.Net.Sockets;

namespace IndustrialS7;

public static class SelfChecks
{
    public static void Offline()
    {
        int checks = 0;
        using Plc plc = S7Examples.CreateDemoPlc(43130);
        Check(!plc.IsConnected && plc.Port == 43130 && plc.Rack == 0 && plc.Slot == 1, "对象配置不建连");
        Check(plc.ReadTimeout == 1000 && plc.WriteTimeout == 1000, "同步等待配置");
        byte[] db = Snap7TestDevice.CreateDb();
        DbSnapshot sample = S7Examples.Decode(db);
        Check(sample.Ready && sample.Mode == 2, "DBX 与 Byte");
        Check(sample.Temperature == -12.3m && sample.Count == 123456, "Int16 与 DInt");
        Check(sample.Measurement == 25.5f && sample.TargetRaw == 250, "Real 与目标字");
        Check(sample.Name == "ATLAS01" && sample.OtherFlag && !sample.RequestFlag, "S7 STRING 头与标志");
        Check(S7.Net.Types.Int.ToByteArray((short)300).Length == 2 && DInt.ToByteArray(300).Length == 4, "写入值CLR类型决定长度");
        DataItem bit = DataItem.FromAddress("DB1.DBX14.1");
        Check(bit.DB == 1 && bit.StartByteAdr == 14 && bit.BitAdr == 1 && bit.VarType == VarType.Bit, "位地址解析");
        DataItem word = DataItem.FromAddress("DB1.DBW12");
        Check(word.StartByteAdr == 12 && word.VarType == VarType.Word, "Word 地址不是Int16语义");
        try { S7Examples.Decode(new byte[25]); throw new Exception("短包未拒绝"); }
        catch (FormatException) { checks++; }
        db[17] = 9;
        try { S7Examples.Decode(db); throw new Exception("字符串长度未拒绝"); }
        catch (FormatException) { checks++; }
        Console.WriteLine($"PASS: {checks} offline S7 checks; no socket/native library loaded.");

        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
    }

    public static async Task NetworkAsync(CancellationToken stop)
    {
        int checks = 0;
        using var device = new Snap7TestDevice();
        using Plc plc = S7Examples.CreateDemoPlc(device.Port);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stop);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await plc.OpenAsync(budget.Token);
            Check(plc.IsConnected && plc.MaxPDUSize > 0, "COTP和S7建立成功");
            byte[] bytes = await plc.ReadBytesAsync(DataType.DataBlock, 1, 0, 26, budget.Token);
            Check(S7Examples.Decode(bytes).Temperature == -12.3m, "原始DB字节读回");
            Check(await plc.ReadAsync("DB1.DBW2", budget.Token) is ushort word && word == 0xFF85, "字符串DBW返回Word");
            Check(await plc.ReadAsync(DataType.DataBlock, 1, 2, VarType.Int, 1, cancellationToken: budget.Token) is short signed && signed == -123,
                "显式VarType.Int返回有符号值");
            Check(await plc.ReadAsync(DataType.DataBlock, 1, 8, VarType.Real, 1, cancellationToken: budget.Token) is float real && real == 25.5f,
                "显式Real解码");
            await S7Examples.WriteDemoTargetAsync(plc, (short)300, budget.Token);
            Check(await plc.ReadAsync("DB1.DBW12", budget.Token) is ushort target && target == 300, "Int16目标写入及读回");
            Check(await plc.ReadAsync("DB1.DBB14", budget.Token) is byte firstFlags && firstFlags == 2, "两字节目标写入未覆盖相邻标志");
            await plc.WriteAsync("DB1.DBX14.0", true, budget.Token);
            Check(await plc.ReadAsync("DB1.DBB14", budget.Token) is byte setFlags && setFlags == 3, "位写入保留同字节其他位");
            await plc.WriteAsync("DB1.DBX14.0", false, budget.Token);
            Check(await plc.ReadAsync("DB1.DBB14", budget.Token) is byte clearedFlags && clearedFlags == 2, "位关闭仍保留其他位");
            var items = new List<DataItem>
            {
                new() { DataType = DataType.DataBlock, DB = 1, StartByteAdr = 2, VarType = VarType.Int, Count = 1 },
                new() { DataType = DataType.DataBlock, DB = 1, StartByteAdr = 8, VarType = VarType.Real, Count = 1 },
            };
            await plc.ReadMultipleVarsAsync(items, budget.Token);
            Check(items[0].Value is short batchInt && batchInt == -123 && items[1].Value is float batchReal && batchReal == 25.5f, "多变量读取");
            try { await plc.ReadBytesAsync(DataType.DataBlock, 2, 0, 2, budget.Token); throw new InvalidOperationException("不存在DB未报错"); }
            catch (Exception error) when (error.GetType() == typeof(Exception)
                && error.Message.Contains("Object does not exist", StringComparison.Ordinal)) { checks++; }
            try { await S7Examples.WriteDemoTargetAsync(plc, (short)1001, budget.Token); throw new Exception("越界目标未拒绝"); }
            catch (ArgumentOutOfRangeException) { checks++; }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await plc.ReadBytesAsync(DataType.DataBlock, 1, 0, 2, cancelled.Token); throw new Exception("取消未生效"); }
            catch (OperationCanceledException) { checks++; }
        }
        finally { plc.Close(); }
        Check(!plc.IsConnected, "显式关闭后对象状态");
        await CancelHandshakeAsync();
        Console.WriteLine($"PASS: {checks} S7.NET+ ↔ Snap7 loopback checks; Windows x64, no physical PLC.");

        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }

        async Task CancelHandshakeAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop);
            cancel.CancelAfter(TimeSpan.FromSeconds(2));
            using Plc opening = S7Examples.CreateDemoPlc(((IPEndPoint)listener.LocalEndpoint).Port);
            Task<TcpClient> accept = listener.AcceptTcpClientAsync(cancel.Token).AsTask();
            Task open = opening.OpenAsync(cancel.Token);
            try
            {
                using TcpClient peer = await accept;
                byte[] hello = new byte[64];
                Check(await peer.GetStream().ReadAsync(hello, cancel.Token) > 0, "已发起握手，测试端故意不回");
                cancel.Cancel();
                try { await open; throw new InvalidOperationException("握手未被取消"); }
                catch (OperationCanceledException) { checks++; }
            }
            finally { opening.Close(); listener.Stop(); }
        }
    }
}
