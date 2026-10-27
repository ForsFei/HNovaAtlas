namespace IndustrialHalcon;

public sealed record FittedLine(ImagePixel Begin, ImagePixel End, double Score);
public sealed record FittedCircle(ImagePixel Center, double RadiusPixels, double Score);
public sealed record LineInstances(int ObjectIndex, IReadOnlyList<FittedLine> Instances);
public sealed record CircleInstances(int ObjectIndex, IReadOnlyList<FittedCircle> Instances);
public sealed record MetrologyDiagnostics(int ObjectIndex, int MeasureContourObjects, IReadOnlyList<ImagePixel> DetectedEdges,
    int ResultContourObjects);
public sealed record PixelDimensions(double DiameterPixels, double CenterToInfiniteLinePixels, double SignedCircleLineClearancePixels);
public enum MetrologyOutcome { NoResult, MissingObject, Ambiguous, DimensionsMeasured }
public sealed record MetrologyReport(MetrologyOutcome Outcome, LineInstances Lines, CircleInstances Circles,
    PixelDimensions? Dimensions, MetrologyDiagnostics? LineDiagnostics, MetrologyDiagnostics? CircleDiagnostics);

// Fixed, uncalibrated 128x128 fixture outputs only. No fitting, edge detection or metrology model implementation.
public static class MetrologyContracts
{
    private static void Header(int index, double[] parameters, double[] scores, int stride)
    {
        ArgumentNullException.ThrowIfNull(parameters); ArgumentNullException.ThrowIfNull(scores);
        if (index is < 0 or > 31 || scores.Length > 2 || parameters.Length != stride * scores.Length ||
            parameters.Any(v => !double.IsFinite(v)) || scores.Any(s => !double.IsFinite(s) || s is < .8 or > 1))
            throw new ArgumentException("Teaching object index, per-shape parameter stride, max two instances and finite score>=0.8 required.");
    }
    private static ImagePixel Point(double row, double column)
    {
        if (!double.IsFinite(row) || !double.IsFinite(column) || row is < 0 or > 127 || column is < 0 or > 127)
            throw new ArgumentException("Fixed-image pixel coordinates required; not a world-coordinate adapter.");
        return new(row, column);
    }
    public static LineInstances ReadLines(int index, double[] parameters, double[] scores)
    {
        Header(index, parameters, scores, 4);
        FittedLine[] lines = new FittedLine[scores.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            int offset = i * 4;
            ImagePixel begin = Point(parameters[offset], parameters[offset + 1]);
            ImagePixel end = Point(parameters[offset + 2], parameters[offset + 3]);
            if (Math.Sqrt(Math.Pow(end.Row - begin.Row, 2) + Math.Pow(end.Column - begin.Column, 2)) < 1e-6)
                throw new ArgumentException("Degenerate line cannot define a perpendicular distance.");
            lines[i] = new(begin, end, scores[i]);
        }
        return new(index, Array.AsReadOnly(lines));
    }
    public static CircleInstances ReadCircles(int index, double[] parameters, double[] scores)
    {
        Header(index, parameters, scores, 3);
        FittedCircle[] circles = new FittedCircle[scores.Length];
        for (int i = 0; i < circles.Length; i++)
        {
            int offset = i * 3;
            ImagePixel center = Point(parameters[offset], parameters[offset + 1]);
            double radius = parameters[offset + 2];
            if (radius is <= 0 or > 48 || center.Row - radius < 0 || center.Row + radius > 127 ||
                center.Column - radius < 0 || center.Column + radius > 127)
                throw new ArgumentException("Positive bounded fully visible fixture circle required; not all HALCON circles.");
            circles[i] = new(center, radius, scores[i]);
        }
        return new(index, Array.AsReadOnly(circles));
    }
    public static MetrologyDiagnostics ReadDiagnostics(int index, int measureContours, double[] rows, double[] columns, int resultContours)
    {
        ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(columns);
        if (index is < 0 or > 31 || rows.Length > 256 || rows.Length != columns.Length || measureContours is < 0 or > 256 ||
            resultContours is < 0 or > 16)
            throw new ArgumentException("Bounded measure/edge/contour diagnostic counts required.");
        ImagePixel[] copied = new ImagePixel[rows.Length];
        for (int i = 0; i < copied.Length; i++) copied[i] = Point(rows[i], columns[i]);
        return new(index, measureContours, Array.AsReadOnly(copied), resultContours);
    }
    public static MetrologyReport Combine(LineInstances lines, CircleInstances circles,
        MetrologyDiagnostics? lineDiagnostics = null, MetrologyDiagnostics? circleDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(lines); ArgumentNullException.ThrowIfNull(circles);
        if (lines.ObjectIndex == circles.ObjectIndex || lineDiagnostics is { } ld && ld.ObjectIndex != lines.ObjectIndex ||
            circleDiagnostics is { } cd && cd.ObjectIndex != circles.ObjectIndex)
            throw new ArgumentException("Line/circle object indices must differ and diagnostic identities must agree.");
        int nl = lines.Instances.Count, nc = circles.Instances.Count;
        MetrologyOutcome outcome = nl > 1 || nc > 1 ? MetrologyOutcome.Ambiguous : nl == 0 && nc == 0 ? MetrologyOutcome.NoResult :
            nl == 0 || nc == 0 ? MetrologyOutcome.MissingObject : MetrologyOutcome.DimensionsMeasured;
        PixelDimensions? dimensions = null;
        if (outcome == MetrologyOutcome.DimensionsMeasured)
        {
            FittedLine line = lines.Instances[0]; FittedCircle circle = circles.Instances[0];
            double dr = line.End.Row - line.Begin.Row, dc = line.End.Column - line.Begin.Column;
            double length = Math.Sqrt(dr * dr + dc * dc);
            double distance = Math.Abs(dr * (circle.Center.Column - line.Begin.Column) - dc * (circle.Center.Row - line.Begin.Row)) / length;
            dimensions = new(2 * circle.RadiusPixels, distance, distance - circle.RadiusPixels);
        }
        return new(outcome, lines, circles, dimensions, lineDiagnostics, circleDiagnostics);
    }
}
