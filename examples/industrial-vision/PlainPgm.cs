using System.Globalization;

using System.IO;

namespace IndustrialVision;

// Strict teaching subset: one P2 image, maxval 255, exact raster count.
public static class PlainPgm
{
    public const int MaxTextLength = 1_048_576;
    public static Mono8Image Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxTextLength) throw new ArgumentException("Teaching text cap exceeded.");
        using IEnumerator<string> tokens = Tokens(text).GetEnumerator();
        string Next() => tokens.MoveNext() ? tokens.Current : throw new FormatException("Incomplete P2 image.");
        int Number() => int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture);
        if (Next() != "P2") throw new FormatException("Only plain PGM P2 supported; not P5/PNG/JPEG.");
        int width = Number(), height = Number();
        int count = Mono8Image.PixelCount(width, height);
        if (Number() != 255) throw new FormatException("Teaching reader requires maxval 255; no implicit scaling.");
        byte[] pixels = new byte[count];
        for (int i = 0; i < count; i++)
        {
            int value = Number();
            if (value > 255) throw new FormatException("Pixel outside Mono8 range.");
            pixels[i] = (byte)value;
        }
        if (tokens.MoveNext()) throw new FormatException("Extra pixels or additional image refused.");
        return Mono8Image.FromPacked(width, height, pixels);
    }
    private static IEnumerable<string> Tokens(string text)
    {
        using StringReader reader = new(text);
        while (reader.ReadLine() is { } line)
        {
            int comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment];
            foreach (string token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) yield return token;
        }
    }
    public static Mono8Image LoadFixture()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "fixtures", "contrast-target.pgm");
        if (new FileInfo(file).Length > MaxTextLength) throw new ArgumentException("Fixture too large.");
        return Parse(File.ReadAllText(file));
    }
}
