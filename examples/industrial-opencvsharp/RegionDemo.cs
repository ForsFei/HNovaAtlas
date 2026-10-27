using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public sealed record ComponentInfo(int Label, int PixelArea, Rect Bounds, double CentroidX, double CentroidY, bool TouchesImageBorder);
public sealed record ContourInfo(int Index, int Next, int Previous, int Child, int Parent, int Depth,
    double PolygonArea, double PerimeterPixels, Rect Bounds, IReadOnlyList<Point> Points);
public enum RegionOutcome { NoForeground, NoSelectedComponents, SelectedComponents }
public sealed record RegionReport(RegionRecipe Recipe, int ForegroundPixels, RegionOutcome Outcome,
    IReadOnlyList<ComponentInfo> Components, IReadOnlyList<ComponentInfo> Selected, IReadOnlyList<ContourInfo> Contours);

public static class RegionDemo
{
    public static Mat Fixture()
    {
        Mat mask = new(24, 32, MatType.CV_8UC1, new Scalar(0));
        try
        {
            for (int y = 4; y <= 13; y++) for (int x = 3; x <= 12; x++) mask.Set(y, x, (byte)255);
            for (int y = 7; y <= 9; y++) for (int x = 6; x <= 8; x++) mask.Set(y, x, (byte)0);
            for (int y = 5; y <= 8; y++) for (int x = 20; x <= 24; x++) mask.Set(y, x, (byte)255);
            mask.Set(18, 28, (byte)255); return mask;
        }
        catch { mask.Dispose(); throw; }
    }
    public static RegionReport Analyze(Mat mask, RegionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(mask); ArgumentNullException.ThrowIfNull(recipe); recipe.Validate();
        byte[] copied = MatBasics.CopyMono8(mask);
        if (copied.Any(v => v is not 0 and not 255)) throw new ArgumentException("This adapter requires explicit 0/255 binary input.");
        int width = mask.Cols, height = mask.Rows;
        using Mat source = mask.Clone(), labels = new(), stats = new(), centroids = new();
        int n = Cv2.ConnectedComponentsWithStats(source, labels, stats, centroids,
            recipe.Connectivity == 4 ? PixelConnectivity.Connectivity4 : PixelConnectivity.Connectivity8, MatType.CV_32SC1);
        if (n < 1 || n > 257 || stats.Rows != n || stats.Cols != 5 || stats.Type() != MatType.CV_32SC1 ||
            centroids.Rows != n || centroids.Cols != 2 || centroids.Type() != MatType.CV_64FC1)
            throw new InvalidOperationException("Bounded component output layout required.");
        List<ComponentInfo> components = [];
        for (int label = 1; label < n; label++) // Label zero is background, not a product.
        {
            int S(ConnectedComponentsTypes column) => stats.At<int>(label, (int)column);
            int x = S(ConnectedComponentsTypes.Left), y = S(ConnectedComponentsTypes.Top);
            int w = S(ConnectedComponentsTypes.Width), h = S(ConnectedComponentsTypes.Height), area = S(ConnectedComponentsTypes.Area);
            PixelLayout.ValidateRoi(width, height, x, y, w, h);
            double cx = centroids.At<double>(label, 0), cy = centroids.At<double>(label, 1);
            if (area < 1 || area > (long)w * h || !double.IsFinite(cx) || !double.IsFinite(cy) || cx < x || cx > x + w - 1 || cy < y || cy > y + h - 1)
                throw new InvalidOperationException("Invalid foreground statistics.");
            components.Add(new(label, area, new(x + recipe.OriginX, y + recipe.OriginY, w, h), cx + recipe.OriginX, cy + recipe.OriginY,
                x == 0 || y == 0 || x + w == width || y + h == height));
        }
        int foreground = Cv2.CountNonZero(source);
        if (components.Sum(v => v.PixelArea) != foreground) throw new InvalidOperationException("Foreground component areas must partition nonzero pixels.");
        Cv2.FindContours(source, out Point[][] contours, out HierarchyIndex[] hierarchy, RetrievalModes.Tree,
            ContourApproximationModes.ApproxSimple, new Point(recipe.OriginX, recipe.OriginY));
        if (contours.Length != hierarchy.Length || contours.Sum(v => (long)v.Length) > 65536)
            throw new InvalidOperationException("Bounded corresponding contour outputs required.");
        int[] depths = RegionContracts.Depths(hierarchy.Select(v => v.Parent).ToArray());
        ContourInfo[] snapshots = new ContourInfo[contours.Length];
        for (int i = 0; i < contours.Length; i++)
        {
            HierarchyIndex link = hierarchy[i];
            if (new[] { link.Next, link.Previous, link.Child }.Any(v => v < -1 || v >= contours.Length) || contours[i].Length == 0)
                throw new InvalidOperationException("Invalid contour links/points.");
            Point[] points = (Point[])contours[i].Clone();
            double area = Cv2.ContourArea(points), perimeter = Cv2.ArcLength(points, true);
            if (!double.IsFinite(area) || !double.IsFinite(perimeter) || area < 0 || perimeter < 0) throw new InvalidOperationException("Finite contour geometry required.");
            snapshots[i] = new(i, link.Next, link.Previous, link.Child, link.Parent, depths[i], area, perimeter,
                Cv2.BoundingRect(points), Array.AsReadOnly(points));
        }
        ComponentInfo[] selected = components.Where(v => v.PixelArea >= recipe.MinimumArea && v.PixelArea <= recipe.MaximumArea &&
            (!recipe.RejectBorder || !v.TouchesImageBorder)).ToArray();
        RegionOutcome outcome = components.Count == 0 ? RegionOutcome.NoForeground : selected.Length == 0 ? RegionOutcome.NoSelectedComponents : RegionOutcome.SelectedComponents;
        // Contours describe the original mask, not a mask rebuilt from selected components. No label/contour index mapping is asserted.
        return new(recipe, foreground, outcome, components.AsReadOnly(), Array.AsReadOnly(selected), Array.AsReadOnly(snapshots));
    }
    public static void Run()
    {
        using Mat mask = Fixture(); RegionReport report = Analyze(mask, new());
        Console.WriteLine($"Foreground={report.ForegroundPixels}, components={report.Components.Count}, selected={report.Selected.Count}, tree contours={report.Contours.Count}; {report.Outcome}, not product OK.");
        foreach (var item in report.Components) Console.WriteLine($"Label {item.Label}: pixel area={item.PixelArea}, bounds={item.Bounds}, centroid=({item.CentroidX},{item.CentroidY})");
        foreach (var item in report.Contours) Console.WriteLine($"Contour {item.Index}: parent={item.Parent}, depth={item.Depth}, polygon area={item.PolygonArea}, closed perimeter={item.PerimeterPixels}, bounds={item.Bounds}");
    }
}
