namespace IndustrialHalcon;

public enum MetrologyScene { Both, LineOnly, CircleOnly, Blank, SmallCircle }
public static class MetrologyFixture
{
    public static int[] Pixels(MetrologyScene scene)
    {
        if (!Enum.IsDefined(scene)) throw new ArgumentOutOfRangeException(nameof(scene));
        int[] pixels = new int[128 * 128];
        for (int row = 0; row < 128; row++) for (int column = 0; column < 128; column++)
        {
            int gray = scene == MetrologyScene.Blank ? 16 : scene == MetrologyScene.CircleOnly || column >= 40 ? 220 : 16;
            if (scene is MetrologyScene.Both or MetrologyScene.CircleOnly or MetrologyScene.SmallCircle)
            {
                int radius = scene == MetrologyScene.SmallCircle ? 8 : 20;
                if ((row - 64) * (row - 64) + (column - 88) * (column - 88) <= radius * radius) gray = 16;
            }
            pixels[row * 128 + column] = gray;
        }
        return pixels;
    }
}
