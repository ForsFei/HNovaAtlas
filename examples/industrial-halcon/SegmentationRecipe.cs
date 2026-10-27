namespace IndustrialHalcon;

// Inclusive original-image pixel centers, not x/y/width/height or a cropped frame.
public readonly record struct PixelRoi(int Row1, int Column1, int Row2, int Column2)
{
    public long Area => ((long)Row2 - Row1 + 1) * ((long)Column2 - Column1 + 1);
    public void Validate(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 1_048_576 ||
            Row1 < 0 || Column1 < 0 || Row2 < Row1 || Column2 < Column1 || Row2 >= height || Column2 >= width)
            throw new ArgumentException("Bounded image and inclusive in-image ROI required; never silently clamp.");
    }
}

public sealed record SegmentationRecipe(PixelRoi Roi, int Low = 200, int High = 255,
    int OpeningWidth = 3, int OpeningHeight = 3, bool FillHoles = true,
    int MinimumArea = 40, int MaximumArea = 60)
{
    public static SegmentationRecipe Default => new(new PixelRoi(2, 2, 13, 13));
    public void Validate(int width, int height)
    {
        Roi.Validate(width, height);
        if (Low < 0 || High > 255 || Low > High ||
            OpeningWidth is < 1 or > 7 || OpeningHeight is < 1 or > 7 ||
            OpeningWidth % 2 == 0 || OpeningHeight % 2 == 0 ||
            MinimumArea < 1 || MaximumArea < MinimumArea || MaximumArea > Roi.Area)
            throw new ArgumentException("Byte interval, odd 1..7 teaching kernels and positive inclusive area limits required.");
    }
}

// Areas describe successive *processed* masks. Not original defect area, millimeters or product OK/NG.
public sealed record SegmentationTrace(double ThresholdArea, double OpenedArea, double FilledArea,
    double ClippedArea, int Components, BlobReport Selected);
