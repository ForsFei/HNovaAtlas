using OpenCvSharp;

namespace IndustrialOpenCvSharp;

public static class RegionChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        using Mat mask = RegionDemo.Fixture(); byte[] before = MatBasics.CopyMono8(mask);
        RegionReport report = RegionDemo.Analyze(mask, new());
        Check(report.ForegroundPixels == 112 && report.Components.Count == 3, "112 pixels partition into three foreground components, background excluded");
        Check(report.Selected.Count == 2 && report.Selected.All(v => v.PixelArea >= 2), "Area filter removes singleton without auto-selecting one product");
        Check(report.Components.Select(v => v.PixelArea).Order().SequenceEqual(new[] { 1, 20, 91 }), "Holed rectangle has91 actual pixels, not100 outer-box pixels");
        ComponentInfo ring = report.Components.Single(v => v.PixelArea == 91);
        Check(ring.Bounds == new Rect(3, 4, 10, 10) && Math.Abs(ring.CentroidX - 687.0 / 91) < 1e-9 && Math.Abs(ring.CentroidY - 778.0 / 91) < 1e-9,
            "Pixel centroid excludes actual dark hole, uses X then Y");
        Check(report.Contours.Count == 4 && report.Contours.Count(v => v.Depth == 0) == 3 && report.Contours.Count(v => v.Depth == 1) == 1, "Tree has three roots plus one hole boundary");
        ContourInfo outer = report.Contours.Single(v => v.Bounds == ring.Bounds);
        ContourInfo hole = report.Contours.Single(v => v.Depth == 1);
        Check(outer.PolygonArea == 81 && outer.PerimeterPixels == 36, "Outer pixel-center polygon area81 differs from component91 and box100");
        Check(hole.Parent == outer.Index && hole.PolygonArea == 14 && hole.Bounds == new Rect(5, 6, 5, 5), "Hole contour follows surrounding boundary, not a count of9 dark pixels");
        Check(report.Contours.Single(v => v.Bounds.Width == 1).PolygonArea == 0, "Singleton foreground can have zero polygon area");
        Check(report.Contours.All(v => v.Points.Count > 0) && MatBasics.CopyMono8(mask).SequenceEqual(before), "Contour snapshots populated and borrowed source unchanged");
        try { ((IList<Point>)outer.Points)[0] = new(0, 0); throw new InvalidOperationException("Read-only expected"); }
        catch (NotSupportedException) { Check(true, "Published contour points read-only"); }
        RegionReport noneSelected = RegionDemo.Analyze(mask, new(MinimumArea: 200));
        Check(noneSelected.Outcome == RegionOutcome.NoSelectedComponents && noneSelected.Components.Count == 3 && noneSelected.Contours.Count == 4,
            "Selection does not rewrite raw mask or discard its contour evidence");
        using Mat blank = new(6, 8, MatType.CV_8UC1, new Scalar(0)); RegionReport empty = RegionDemo.Analyze(blank, new());
        Check(empty.Outcome == RegionOutcome.NoForeground && empty.Contours.Count == 0, "Blank mask returns no foreground, not product OK");
        using Mat full = new(3, 4, MatType.CV_8UC1, new Scalar(255)); RegionReport fullReport = RegionDemo.Analyze(full, new());
        Check(fullReport.Components.Single().PixelArea == 12 && fullReport.Components[0].TouchesImageBorder,
            "All-white mask skips absent-background statistics and retains foreground");
        using Mat invalid = new(2, 2, MatType.CV_8UC1, new Scalar(16));
        Reject(() => RegionDemo.Analyze(invalid, new()), "Nonbinary gray input refused by adapter policy");
        using Mat diagonal = new(3, 3, MatType.CV_8UC1, new Scalar(0)); diagonal.Set(0, 0, (byte)255); diagonal.Set(1, 1, (byte)255);
        Check(RegionDemo.Analyze(diagonal, new(Connectivity: 4)).Components.Count == 2 && RegionDemo.Analyze(diagonal, new()).Components.Count == 1,
            "Four/eight connectivity actually changes diagonal labels");
        RegionReport border = RegionDemo.Analyze(diagonal, new(MinimumArea: 1, RejectBorder: true));
        Check(border.Components.Single().TouchesImageBorder && border.Selected.Count == 0, "Border-touch policy remains explicit");
        RegionReport shifted = RegionDemo.Analyze(mask, new(OriginX: 100, OriginY: 200));
        Check(shifted.Components.Single(v => v.PixelArea == 91).Bounds == new Rect(103, 204, 10, 10) &&
            shifted.Contours.Any(v => v.Bounds == new Rect(103, 204, 10, 10)), "Origin translates component geometry and contour points consistently");
        Check(shifted.Components.Single(v => v.PixelArea == 91).CentroidX == ring.CentroidX + 100 && shifted.ForegroundPixels == report.ForegroundPixels,
            "Coordinate offset does not change area or pretend to calibrate millimeters");
        Cv2.FindContours(mask, out Point[][] external, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Check(external.Length == 3, "External mode omits hole contour");
        Cv2.FindContours(mask, out Point[][] dense, out _, RetrievalModes.Tree, ContourApproximationModes.ApproxNone);
        Check(dense.Length == 4 && dense.Any(v => v.Length > 4), "ApproxNone preserves more boundary points, not more objects");
        Check(Cv2.ContourArea(outer.Points.Reverse(), true) == -Cv2.ContourArea(outer.Points, true), "Oriented area sign depends on point order");
        using Mat nested = new(31, 31, MatType.CV_8UC1, new Scalar(0));
        for (int y = 2; y <= 28; y++) for (int x = 2; x <= 28; x++) nested.Set(y, x, (byte)255);
        for (int y = 6; y <= 24; y++) for (int x = 6; x <= 24; x++) nested.Set(y, x, (byte)0);
        for (int y = 10; y <= 20; y++) for (int x = 10; x <= 20; x++) nested.Set(y, x, (byte)255);
        RegionReport nestedReport = RegionDemo.Analyze(nested, new());
        Check(nestedReport.Components.Count == 2 && nestedReport.Contours.Select(v => v.Depth).Order().SequenceEqual(new[] { 0, 1, 2 }),
            "Island inside hole is foreground depth2, not another hole");
        Cv2.FindContours(nested, out Point[][] ccomp, out HierarchyIndex[] links, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
        Check(ccomp.Length == 3 && links.Count(v => v.Parent == -1) == 2, "CComp flattens nested island to top-level component contour");
        using Mat view = new(mask, new Rect(2, 3, 12, 12)); RegionReport roi = RegionDemo.Analyze(view, new(OriginX: 2, OriginY: 3));
        Check(roi.Components.Single().Bounds == ring.Bounds && roi.Contours.Single(v => v.Depth == 0).Bounds == ring.Bounds,
            "ROI local coordinates restored exactly once with supplied origin");
        Console.WriteLine($"Native region/contour checks passed: {count}. Labeling/topology/pixel geometry only; no tracking, subpixel measurement or product acceptance.");
    }
}
