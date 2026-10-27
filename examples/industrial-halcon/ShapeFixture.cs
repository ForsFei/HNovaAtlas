namespace IndustrialHalcon;

public enum ShapeScene { Reference, Translation, QuarterTurn, Blank, Double }

public static class ShapeFixture
{
    public static PixelRoi ModelRoi => new(24, 24, 63, 63);
    public static PixelRoi InspectionRoi => new(46, 42, 48, 46); // Center=(47,44), area=15, inside middle arm.
    public static int[] Pixels(ShapeScene scene)
    {
        if (!Enum.IsDefined(scene)) throw new ArgumentOutOfRangeException(nameof(scene));
        int[] pixels = Enumerable.Repeat(16, 96 * 96).ToArray();
        if (scene == ShapeScene.Blank) return pixels;
        for (int r = 28; r <= 59; r++) for (int c = 28; c <= 59; c++)
        {
            bool mark = c <= 35 || r <= 35 || r is >= 44 and <= 49 && c <= 49;
            if (!mark) continue;
            if (scene == ShapeScene.Double)
            {
                pixels[r * 96 + c - 18] = 220;
                pixels[r * 96 + c + 26] = 220;
            }
            else
            {
                int row = scene == ShapeScene.QuarterTurn ? 95 - c : r;
                int column = scene == ShapeScene.QuarterTurn ? r : c;
                if (scene is ShapeScene.Translation or ShapeScene.QuarterTurn) { row += 4; column += 6; }
                pixels[row * 96 + column] = 220;
            }
        }
        return pixels;
    }
}
