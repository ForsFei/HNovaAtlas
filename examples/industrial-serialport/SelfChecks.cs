using System.Buffers.Binary;
using System.IO.Ports;
using System.Text;

namespace IndustrialSerialPort;

internal static class SelfChecks
{
    public static void Run()
    {
        using SerialPort port = DemoPort.Create("COM99", 9600);
        Check(!port.IsOpen, "创建对象不打开端口");
        Check(port.BaudRate == 9600 && port.DataBits == 8
            && port.Parity == Parity.None && port.StopBits == StopBits.One, "8N1 配置");
        Check(port.Handshake == Handshake.None && !port.DtrEnable && !port.RtsEnable, "演示流控配置");
        Check(port.Encoding.CodePage == Encoding.ASCII.CodePage && port.NewLine == "\r\n", "文本编码与结束符");
        Check(port.ReadTimeout == 1500 && port.WriteTimeout == 1500, "有限读写超时");

        Check(TemperatureProtocol.ParseResponse("TEMP=253") == 25.3m, "缩放");
        Check(TemperatureProtocol.ParseResponse("TEMP=-40") == -4m, "有符号值");
        Check(TemperatureProtocol.ParseResponse("TEMP=-400") == -40m, "下限");
        Check(TemperatureProtocol.ParseResponse("TEMP=1250") == 125m, "上限");
        foreach (string malformed in new[] { "TEMP=x", "OTHER=253", "TEMP=2000", "TEMP=25.3", "TEMP= 253", "TEMP=253\r\n" })
        {
            bool rejected = false;
            try { TemperatureProtocol.ParseResponse(malformed); }
            catch (FormatException) { rejected = true; }
            Check(rejected, $"拒绝非法负载：{malformed}");
        }

        byte[] payload = [0x01, 0x2C];
        Check(BinaryPrimitives.ReadInt16BigEndian(payload) == 300, "数据契约的大端解析");
        Check(Encoding.ASCII.GetBytes("300").SequenceEqual(new byte[] { 0x33, 0x30, 0x30 }), "文本数字与二进制不同");
        Console.WriteLine("离线断言通过：端口配置、温度协议与字节解析；未打开任何串口。");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
