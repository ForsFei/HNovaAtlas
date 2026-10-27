using System.Globalization;

namespace IndustrialSerialPort;

public static class TemperatureProtocol
{
    public const string Request = "READ TEMP";

    // ReadLine has already removed CRLF. This parser accepts just the frame payload.
    public static decimal ParseResponse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.StartsWith("TEMP=", StringComparison.Ordinal)
            || !int.TryParse(line.AsSpan(5), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out int raw)
            || raw is < -400 or > 1250)
            throw new FormatException("期待 TEMP=<整数>，范围 -400～1250，单位 0.1 °C。");
        return raw / 10m;
    }
}
