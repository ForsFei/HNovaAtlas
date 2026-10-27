using System.Buffers.Binary;

namespace IndustrialModbus;

// For inspection/offline checks only. Real client framing is owned by NModbus.
public static class ModbusFrames
{
    public static byte[] Tcp(ushort transaction, byte unit, ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length is < 1 or > 253) throw new ArgumentOutOfRangeException(nameof(pdu));
        byte[] frame = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, transaction);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(pdu.Length + 1));
        frame[6] = unit;
        pdu.CopyTo(frame.AsSpan(7));
        return frame;
    }

    public static ushort Crc16(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }
}
