namespace IndustrialHalcon;

public enum BlobOutcome { NoCandidates, Candidates }
public sealed record BlobFeature(double AreaPixels, double RowPixels, double ColumnPixels);
public sealed record BlobReport(BlobOutcome Outcome, int ImageWidth, int ImageHeight, IReadOnlyList<BlobFeature> Candidates);

// Pure managed output-boundary validation. This does not implement/replace any HALCON operator.
public static class BlobReadback
{
    public static double[] CopyUsedValues(double[] storage, int usedLength)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (usedLength < 0 || usedLength > 64 || usedLength > storage.Length) throw new ArgumentOutOfRangeException(nameof(usedLength));
        return storage.AsSpan(0, usedLength).ToArray(); // HTuple.DArr capacity can exceed HTuple.Length.
    }
    public static BlobReport FromArrays(int width, int height, int objectCount, double minimumArea, double maximumArea,
        double[] areas, double[] rows, double[] columns)
    {
        ArgumentNullException.ThrowIfNull(areas); ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(columns);
        long pixels = (long)width * height;
        if (width <= 0 || height <= 0 || pixels > 1_048_576 || objectCount is < 0 or > 64
            || !double.IsFinite(minimumArea) || !double.IsFinite(maximumArea)
            || minimumArea < 1 || maximumArea < minimumArea || maximumArea > pixels)
            throw new ArgumentException("Bounded image, object count 0..64 and positive finite pixel-area interval required.");
        if (areas.Length != objectCount || rows.Length != objectCount || columns.Length != objectCount)
            throw new ArgumentException("Iconic object count and all three numeric tuple lengths must agree.");
        BlobFeature[] copied = new BlobFeature[objectCount];
        for (int i = 0; i < objectCount; i++)
        {
            double area = areas[i], row = rows[i], column = columns[i];
            if (!double.IsFinite(area) || area != Math.Truncate(area) || area < minimumArea || area > maximumArea
                || !double.IsFinite(row) || row < 0 || row > height - 1
                || !double.IsFinite(column) || column < 0 || column > width - 1)
                throw new ArgumentException("Finite area in the selected interval and in-image row/column required.");
            copied[i] = new(area, row, column);
        }
        return new(objectCount == 0 ? BlobOutcome.NoCandidates : BlobOutcome.Candidates, width, height, Array.AsReadOnly(copied));
    }
}
