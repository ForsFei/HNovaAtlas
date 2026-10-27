using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace IndustrialModbusMonitor;

public enum RegisterOrder { ABCD, BADC, CDAB, DCBA }

public static class ModbusValueCodec
{
    public static uint UInt32(ushort first, ushort second, RegisterOrder order)
    {
        byte[] bytes = [(byte)(first >> 8), (byte)first, (byte)(second >> 8), (byte)second];
        int[] indices = order switch
        {
            RegisterOrder.ABCD => [0, 1, 2, 3],
            RegisterOrder.BADC => [1, 0, 3, 2],
            RegisterOrder.CDAB => [2, 3, 0, 1],
            RegisterOrder.DCBA => [3, 2, 1, 0],
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
        return BinaryPrimitives.ReadUInt32BigEndian(indices.Select(index => bytes[index]).ToArray());
    }

    public static int Int32(ushort first, ushort second, RegisterOrder order) => unchecked((int)UInt32(first, second, order));

    public static float Float32(ushort first, ushort second, RegisterOrder order)
    {
        float value = BitConverter.Int32BitsToSingle(Int32(first, second, order));
        if (!float.IsFinite(value)) throw new FormatException("该测量契约不接受 NaN 或 Infinity。");
        return value;
    }

    public static bool Bit(ushort value, int bit)
    {
        if (bit is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(bit));
        return (value & (1 << bit)) != 0;
    }

    // Contract: high byte first within each register, printable ASCII, trailing NUL/space padding.
    public static string Ascii(ReadOnlySpan<ushort> registers)
    {
        byte[] bytes = new byte[registers.Length * 2];
        for (int index = 0; index < registers.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(index * 2), registers[index]);
        bool padding = false;
        foreach (byte value in bytes)
        {
            if (value == 0) padding = true;
            else if (value is < 0x20 or > 0x7E || (padding && value != 0x20))
                throw new FormatException("非法 ASCII 字符或 NUL 填充后仍有正文。");
        }
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    }

    public static bool TrySetpoint(string text, out ushort raw, out string error)
    {
        raw = 0;
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal celsius)
            || celsius is < 0 or > 100 || celsius * 10 != decimal.Truncate(celsius * 10))
        {
            error = "目标值须为 0～100，精确到 0.1，使用小数点；不自动四舍五入。";
            return false;
        }
        raw = (ushort)(celsius * 10);
        error = "";
        return true;
    }
}
