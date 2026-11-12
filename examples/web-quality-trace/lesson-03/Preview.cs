static class Preview
{
    // The evidence reader has already verified the P5 header and pixel count.
    public static byte[] ToBmp(byte[] pgm, int width, int height)
    {
        int stride = (width * 3 + 3) & ~3;
        int pixelOffset = pgm.Length - width * height;
        using var output = new MemoryStream(54 + stride * height);
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0x4d42);
        writer.Write(54 + stride * height);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(stride * height);
        writer.Write(new byte[16]);

        var row = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte level = pgm[pixelOffset + y * width + x];
                row[x * 3] = row[x * 3 + 1] = row[x * 3 + 2] = level;
            }

            writer.Write(row);
        }

        return output.ToArray();
    }
}
