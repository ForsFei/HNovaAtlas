using System.Buffers.Binary;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Serial;

namespace IndustrialModbus;

public static class SelfChecks
{
    public static async Task RunAsync()
    {
        int checks = 0;
        Check(RegisterCodec.SignedTenths(0xFF85) == -12.3m, "有符号与缩放");
        Check(RegisterCodec.SignedTenths(0x7FFF) == 3276.7m, "Int16 上界");
        Check(RegisterCodec.UInt32HighWordFirst(1, 2) == 65538, "高字在前 UInt32");
        Check(RegisterCodec.FloatHighWordFirst(0x41C8, 0) == 25f, "高字在前 Float32");
        Check(ModbusFrames.Crc16(Convert.FromHexString("01030000000A")) == 0xCDC5, "RTU CRC 黄金向量，线上 C5 CD");
        Check(ModbusFrames.Crc16(Convert.FromHexString("010300000002")) == 0x0BC4, "教程 RTU 读两个寄存器，线上 C4 0B");
        Check(Convert.ToHexString(ModbusFrames.Tcp(1, 1, Convert.FromHexString("0300000002"))) == "000100000006010300000002",
            "MBAP 长度包括 Unit ID 和 PDU");

        // Construct and dispose only; no port.Open(), no COM traffic.
        using (var port = new SerialPort("COM3", 9600, Parity.Even, 8, StopBits.One))
        using (IModbusSerialMaster master = new ModbusFactory().CreateRtuMaster(port))
        {
            ModbusExamples.Configure(master);
            Check(!port.IsOpen && port.ReadTimeout == 400, "RTU 配置检查不打开串口");
            Check(master.Transport.Retries == 0 && master.Transport.SlaveBusyUsesRetryCount, "普通和 Busy 重试受限");
        }

        await using (var device = new LoopbackModbusDevice())
        {
            await ModbusExamples.ExecuteTcpAsync(device.Endpoint, (master, _) =>
            {
                Check(master.ReadCoils(1, 0, 8).All(value => !value), "FC01 初值");
                Check(master.ReadInputs(1, 0, 3).SequenceEqual(new[] { true, false, true }), "FC02 离散输入");
                Check(master.ReadHoldingRegisters(1, 0, 2).SequenceEqual(new ushort[] { 250, 1 }), "FC03 保持寄存器");
                Check(master.ReadInputRegisters(1, 0, 1)[0] == 0xFF85, "FC04 输入寄存器，地址0而非30001");
                master.WriteSingleCoil(1, 0, true);
                Check(master.ReadCoils(1, 0, 1)[0], "FC05 单线圈写入并读回");
                master.WriteSingleRegister(1, 0, 300);
                Check(master.ReadHoldingRegisters(1, 0, 1)[0] == 300, "FC06 单寄存器写入并读回");
                master.WriteMultipleCoils(1, 2, [true, false, true]);
                Check(master.ReadCoils(1, 0, 5).SequenceEqual(new[] { true, false, true, false, true }), "FC0F 位顺序");
                master.WriteMultipleRegisters(1, 1, [2, 7]);
                Check(master.ReadHoldingRegisters(1, 0, 3).SequenceEqual(new ushort[] { 300, 2, 7 }), "FC10 多寄存器写入");
                try { master.ReadHoldingRegisters(1, 8, 1); throw new Exception("预期非法地址"); }
                catch (SlaveException error) { Check(error.SlaveExceptionCode == 2 && error.FunctionCode == 0x83, "异常响应83/02"); }
                Check(master.ReadHoldingRegisters(1, 0, 1)[0] == 300, "协议异常后服务器仍能响应有效地址");
                return true;
            }, TimeSpan.FromSeconds(4));
            Check(device.Requests >= 14, "真实回环 Modbus TCP 报文交换");
            Check((await RawAsync(device.Endpoint, "4100000001")).SequenceEqual(new byte[] { 0xC1, 1 }), "不支持功能码返回01");
            Check((await RawAsync(device.Endpoint, "0300000000")).SequenceEqual(new byte[] { 0x83, 3 }), "数量0返回03");
            Check((await RawAsync(device.Endpoint, "0500001234")).SequenceEqual(new byte[] { 0x85, 3 }), "单线圈只允许0000/FF00");
            Check((await RawAsync(device.Endpoint, "10000700020400010002")).SequenceEqual(new byte[] { 0x90, 2 }), "跨边界写入先拒绝");
            Check(await ModbusExamples.ExecuteTcpAsync(device.Endpoint,
                (master, _) => master.ReadHoldingRegisters(1, 7, 1)[0], TimeSpan.FromSeconds(2)) == 0,
                "非法多寄存器写入没有部分修改第7号点");
        }

        await using (var cancelled = new LoopbackModbusDevice())
        {
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            await Throws<OperationCanceledException>(() => Query(cancelled, stop.Token), "提前取消");
            Check(cancelled.Requests == 0, "提前取消不发送请求");
        }

        await using (var busy = new LoopbackModbusDevice(DeviceFault.Busy))
        {
            await Throws<SlaveException>(() => Query(busy), "Busy 有限退出");
            Check(busy.Requests == 1, "Retries0与BusyUsesRetryCount避免自动重发");
        }
        await using (var wrong = new LoopbackModbusDevice(DeviceFault.WrongTransaction))
        {
            await Throws<IOException>(() => Query(wrong), "事务编号不匹配");
            Check(wrong.Requests == 1, "错误事务不重发");
        }
        await using (var closed = new LoopbackModbusDevice(DeviceFault.Close))
            await Throws<IOException>(() => Query(closed), "对端关闭");
        await using (var silent = new LoopbackModbusDevice(DeviceFault.Silent))
        {
            using var stop = new CancellationTokenSource(100);
            await Throws<OperationCanceledException>(() => Query(silent, stop.Token), "取消关闭底层TCP并等待同步工作结束");
            Check(silent.Requests <= 1, "取消后不重发");
        }
        await using (var slow = new LoopbackModbusDevice(DeviceFault.SlowBytes))
            await Throws<TimeoutException>(() => ModbusExamples.ExecuteTcpAsync(slow.Endpoint,
                (master, _) => master.ReadHoldingRegisters(1, 0, 1), TimeSpan.FromMilliseconds(200)), "持续零星字节不重置总期限");
        await using (var acknowledge = new LoopbackModbusDevice(DeviceFault.Acknowledge))
        {
            await Throws<TimeoutException>(() => ModbusExamples.ExecuteTcpAsync(acknowledge.Endpoint,
                (master, _) => master.ReadHoldingRegisters(1, 0, 1), TimeSpan.FromMilliseconds(200)), "ACK读等待也受外部总期限限制");
            Check(acknowledge.Requests == 1, "ACK不会被包装器自动重发");
        }
        Console.WriteLine($"PASS: {checks} checks; NModbus TCP loopback + offline RTU configuration. No COM opened.");

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        async Task Throws<T>(Func<Task> work, string message) where T : Exception
        {
            try { await work(); }
            catch (T) { checks++; return; }
            throw new InvalidOperationException($"{message}: 预期 {typeof(T).Name}");
        }
    }

    private static Task<ushort[]> Query(LoopbackModbusDevice device, CancellationToken stop = default) =>
        ModbusExamples.ExecuteTcpAsync(device.Endpoint, (master, _) => master.ReadHoldingRegisters(1, 0, 1), TimeSpan.FromSeconds(2), stop);

    private static async Task<byte[]> RawAsync(IPEndPoint endpoint, string pdu)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port, stop.Token);
        using NetworkStream stream = client.GetStream();
        await stream.WriteAsync(ModbusFrames.Tcp(7, 1, Convert.FromHexString(pdu)), stop.Token);
        byte[] header = new byte[7];
        await stream.ReadExactlyAsync(header, stop.Token);
        byte[] response = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1];
        await stream.ReadExactlyAsync(response, stop.Token);
        return response;
    }
}
