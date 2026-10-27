namespace IndustrialModbus;

public static class RegisterCodec
{
    public static decimal SignedTenths(ushort raw) => unchecked((short)raw) / 10m;
    public static uint UInt32HighWordFirst(ushort high, ushort low) => ((uint)high << 16) | low;
    public static float FloatHighWordFirst(ushort high, ushort low) =>
        BitConverter.Int32BitsToSingle(unchecked((int)UInt32HighWordFirst(high, low)));
}
