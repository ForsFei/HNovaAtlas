using System.Buffers.Binary;

namespace IndustrialMc;

// Bounded teaching subset: 3E binary, Q/L device specification, direct CPU route.
internal static class McFrames
{
    public const int MaxBody = 2048;
    public static ushort U16(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    public static void Put16(Span<byte> bytes, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);

    public static int RequestLength(ReadOnlySpan<byte> header)
    {
        if (header.Length != 9 || header[0] != 0x50 || header[1] != 0 ||
            !header.Slice(2, 5).SequenceEqual(new byte[] { 0, 255, 255, 3, 0 }))
            throw new InvalidDataException("Only binary 3E / direct CPU route is supported here.");
        int length = U16(header[7..]);
        if (length is < 12 or > MaxBody) throw new InvalidDataException("Request body outside demo bounds.");
        return length;
    }

    public static byte[] Response(ushort endCode, byte[] payload)
    {
        if (payload.Length > MaxBody - 2) throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] result = new byte[11 + payload.Length];
        new byte[] { 0xD0, 0, 0, 255, 255, 3, 0 }.CopyTo(result, 0);
        Put16(result.AsSpan(7), checked((ushort)(2 + payload.Length)));
        Put16(result.AsSpan(9), endCode);
        payload.CopyTo(result, 11);
        return result;
    }

    public static byte[] PackBits(ReadOnlySpan<bool> values)
    {
        byte[] packed = new byte[(values.Length + 1) / 2];
        for (int i = 0; i < values.Length; i++)
            if (values[i]) packed[i / 2] |= (byte)(i % 2 == 0 ? 0x10 : 0x01);
        return packed;
    }

    public static bool[] UnpackBits(ReadOnlySpan<byte> packed, int count)
    {
        if (count < 1 || packed.Length != (count + 1) / 2) throw new InvalidDataException("Invalid bit length.");
        bool[] values = new bool[count];
        for (int i = 0; i < packed.Length * 2; i++)
        {
            int nibble = i % 2 == 0 ? packed[i / 2] >> 4 : packed[i / 2] & 15;
            if (nibble > 1 || (i >= count && nibble != 0)) throw new InvalidDataException("Invalid binary bit nibble/padding.");
            if (i < count) values[i] = nibble == 1;
        }
        return values;
    }
}
