using System.Globalization;
using System.Text;
using SerialFraming;

namespace IndustrialNetworking;

public sealed record TemperatureReply(string RequestId, decimal Temperature);

public static class WireProtocol
{
    public const int MaxPayload = 96;
    public static string NewId() => Guid.NewGuid().ToString("N");
    public static byte[] Request(string id) => Encode($"READ {ValidateId(id)} TEMP");
    public static byte[] Response(string id) => Encode($"TEMP {ValidateId(id)} 253");

    public static byte[] Encode(string payload)
    {
        if (payload.Length is 0 or > MaxPayload || payload.Any(c => c is < ' ' or > '~'))
            throw new FormatException("载荷必须是 1～96 字节可打印 ASCII。");
        return Encoding.ASCII.GetBytes(payload + "\r\n");
    }

    public static string ParseRequest(string frame)
    {
        string[] fields = frame.Split(' ');
        if (fields.Length != 3 || fields[0] != "READ" || fields[2] != "TEMP")
            throw new FormatException("期待 READ <32位请求号> TEMP。");
        return ValidateId(fields[1]);
    }

    public static TemperatureReply ParseResponse(string frame)
    {
        string[] fields = frame.Split(' ');
        if (fields.Length != 3 || fields[0] != "TEMP"
            || !int.TryParse(fields[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int raw)
            || raw is < -400 or > 1250)
            throw new FormatException("温度响应格式或量程无效。");
        return new(ValidateId(fields[1]), raw / 10m);
    }

    public static string DecodeDatagram(ReadOnlySpan<byte> bytes)
    {
        // A new decoder per datagram: never concatenate two UDP messages.
        if (bytes.Length > MaxPayload + 2) throw new FormatException("数据报超出教学协议上限。");
        var decoder = new CrlfFrameDecoder(MaxPayload);
        IReadOnlyList<string> frames = decoder.Feed(bytes);
        if (frames.Count != 1 || decoder.BufferedBytes != 0)
            throw new FormatException("一个 UDP 数据报必须恰好包含一帧。");
        return frames[0];
    }

    private static string ValidateId(string id)
    {
        if (id.Length != 32 || id.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new FormatException("请求号必须是 32 位小写十六进制。");
        return id;
    }
}
