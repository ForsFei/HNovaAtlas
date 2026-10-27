namespace IndustrialHalcon;

public enum MeasureScene { BrightStrip, DarkStrip, TwoStrips, Blank, SingleStep, HorizontalStrip }
public static class MeasureFixture
{
    public static int[] Pixels(MeasureScene scene)
    {
        if (!Enum.IsDefined(scene)) throw new ArgumentOutOfRangeException(nameof(scene));
        int[] pixels = new int[128 * 96];
        for (int row = 0; row < 96; row++) for (int column = 0; column < 128; column++)
        {
            bool strip = row is >= 16 and <= 79 && column is >= 48 and <= 79;
            pixels[row * 128 + column] = scene switch
            {
                MeasureScene.BrightStrip => strip ? 220 : 16,
                MeasureScene.DarkStrip => strip ? 16 : 220,
                MeasureScene.TwoStrips => row is >= 16 and <= 79 && (column is >= 32 and <= 47 or >= 72 and <= 87) ? 220 : 16,
                MeasureScene.SingleStep => column >= 64 ? 220 : 16,
                MeasureScene.HorizontalStrip => row is >= 32 and <= 63 && column is >= 16 and <= 111 ? 220 : 16,
                _ => 16
            };
        }
        return pixels;
    }
}
