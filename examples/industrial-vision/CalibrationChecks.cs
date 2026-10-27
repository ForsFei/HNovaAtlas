namespace IndustrialVision;

public static class CalibrationChecks
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static bool Near(double a, double b, double tolerance = 1e-10) => Math.Abs(a - b) <= tolerance;
    private static bool Near(PlanePoint a, PlanePoint b) => Near(a.Xmm, b.Xmm) && Near(a.Ymm, b.Ymm);
    private static bool Near(ImagePoint a, ImagePoint b) => Near(a.Column, b.Column) && Near(a.Row, b.Row);
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {label}");
    }
    private static CalibrationBinding Binding => new("synthetic-fixed-camera", 1, "synthetic-mono8-centers", "fixture-plane-mm", 8, 6);
    private static PlanePoint Truth(ImagePoint p) => new(10 + .1 * p.Column + .02 * p.Row, 20 + .01 * p.Column - .2 * p.Row);
    private static CalibrationPair Pair(string name, double column, double row) => new(name, new(column, row), Truth(new(column, row)));
    private static CalibrationPair[] Training() => [Pair("fit-A", 0, 0), Pair("fit-B", 7, 0), Pair("fit-C", 0, 5)];
    private static CalibrationPair[] Validation()
    {
        CalibrationPair a = Pair("check-1", 1, 1), b = Pair("check-2", 2, 1), c = Pair("check-3", 1, 2);
        return [a with { Plane = new(a.Plane.Xmm + .03, a.Plane.Ymm + .04) }, b,
            c with { Plane = new(c.Plane.Xmm - .06, c.Plane.Ymm) }];
    }
    public static void Run()
    {
        count = 0;
        CalibrationBinding binding = Binding;
        CalibrationPair[] training = Training();
        PlanarAffineCalibration model = PlanarAffineCalibration.FitThree(binding, training);
        AffineCoefficients m = model.Coefficients;
        Check(Near(m.M11, .1) && Near(m.M12, .02) && Near(m.M21, .01) && Near(m.M22, -.2), "Known anisotropic/skewed synthetic linear map recovered");
        Check(Near(m.Tx, 10) && Near(m.Ty, 20), "Translation recovered in explicitly named millimetre plane");
        Check(model.ImageTriangleShape > 0 && model.PlaneTriangleShape > 0 && model.Binding == binding, "Model retains binding and two triangle geometry scores");
        foreach (CalibrationPair point in training)
            Check(model.TryMap(point.Image, binding, out PlanePoint world) && Near(world, point.Plane), $"Three-point exact fit reproduces {point.Name}, not independent validation");
        Check(model.TryMap(new(2, 1), binding, out PlanePoint mapped) && Near(mapped, new(10.22, 19.82)), "Subsequent in-triangle point maps by full affine formula");
        Check(model.TryMap(new(1.25, 1.5), binding, out PlanePoint subpixel) && Near(subpixel, Truth(new(1.25, 1.5))), "Subpixel coordinates are not rounded to integers");
        Check(model.TryMap(new(1, 2), binding, out PlanePoint swapped) && !Near(swapped, mapped), "Column/row interchange produces different physical coordinates");
        Check(model.TryMap(new(3.5, 2.5), binding, out _), "Triangle edge is part of numerical support");
        Check(!model.TryMap(new(7, 5), binding, out _), "In-image but outside training triangle is refused, no extrapolation");
        Check(!model.TryMap(new(-.1, 1), binding, out _) && !model.TryMap(new(8, 0), binding, out _), "Outside declared pixel-center domain refused");
        Check(!model.TryMap(new(double.NaN, 1), binding, out _) && !model.TryMap(new(1, double.PositiveInfinity), binding, out _), "Nonfinite image point refused");
        Check(model.TryMap(new(2, 1), binding with { }, out _), "Binding is immutable value identity, not object-reference identity");
        Check(!model.TryMap(new(2, 1), binding with { SetupId = "another-camera" }, out _), "Different setup label refused");
        Check(!model.TryMap(new(2, 1), binding with { GeometryRevision = 2 }, out _), "Changed geometry revision refused");
        Check(!model.TryMap(new(2, 1), binding with { PixelSpace = "undistorted-pixels" }, out _), "Raw/undistorted coordinate spaces cannot be relabelled interchangeably");
        Check(!model.TryMap(new(2, 1), binding with { PlaneFrame = "robot-base" }, out _), "Different target frame refused");
        Check(!model.TryMap(new(2, 1), binding with { Width = 9 }, out _), "Changed image geometry refused");
        Check(!model.TryMap(new(2, 1), null!, out _), "Missing live binding refused");
        Check(model.TryUnmap(mapped, binding, out ImagePoint recovered) && Near(recovered, new(2, 1)), "Inverse maps corresponding millimetre point back to image");
        Check(!model.TryUnmap(Truth(new(7, 5)), binding, out _), "Inverse also rejects extrapolation");
        Check(!model.TryUnmap(new(double.NaN, 0), binding, out _) && !model.TryUnmap(new(2e6, 0), binding, out _), "Nonfinite/out-of-policy physical inputs refused");
        Check(!model.TryUnmap(mapped, binding with { GeometryRevision = 2 }, out _), "Inverse requires original geometry binding too");
        double distance = model.MeasureDistance(new(1, 1), new(3, 2), binding);
        Check(Near(distance, Math.Sqrt(.22 * .22 + .18 * .18)), "Distance uses both mapped endpoint coordinates, not one mm-per-pixel scalar");
        Check(Near(model.MeasureDistance(new(1, 1), new(1, 1), binding), 0), "Identical endpoints have zero physical distance");
        Reject<ArgumentException>(() => model.MeasureDistance(new(1, 1), new(7, 5), binding), "Distance refuses unsupported endpoint");
        double bound = model.PixelPerturbationBound(.2);
        Check(Near(bound, .2 * Math.Sqrt(.01 + .0004 + .0001 + .04)), "Pixel perturbation upper bound uses fixed linear-map Frobenius norm");
        Check(Near(model.PixelPerturbationBound(0), 0), "Zero input perturbation has zero linear contribution, not zero total uncertainty");
        Reject<ArgumentOutOfRangeException>(() => model.PixelPerturbationBound(-1), "Negative pixel radius refused");
        Reject<ArgumentOutOfRangeException>(() => model.PixelPerturbationBound(double.NaN), "Nonfinite pixel radius refused");
        Reject<ArgumentOutOfRangeException>(() => model.PixelPerturbationBound(1001), "Teaching perturbation cap enforced");

        CalibrationErrorReport report = model.Evaluate(Validation(), binding);
        Check(report.Count == 3 && report.Points.Count == 3, "Holdout report keeps every independent check residual");
        Check(Near(report.Points[0].ErrorXmm, -.03) && Near(report.Points[0].ErrorYmm, -.04)
            && Near(report.Points[0].RadialErrorMm, .05), "Residual direction is prediction minus independent reference");
        Check(Near(report.RmsMm, Math.Sqrt((.05 * .05 + .06 * .06) / 3)) && Near(report.MaxMm, .06), "RMS radial error and maximum computed independently, not mean radial error");
        Check(Near(report.MeanErrorXmm, .01) && Near(report.MeanErrorYmm, -.04 / 3), "Signed average errors expose systematic offset direction");
        Check(model.TryMap(new(2, 1), binding, out PlanePoint afterReport) && Near(afterReport, mapped), "Evaluation never refits or moves calibration to reduce check errors");
        Reject<NotSupportedException>(() => ((IList<CalibrationResidual>)report.Points)[0] = report.Points[1], "Report residual collection cannot be mutated through IList");
        Reject<ArgumentException>(() => model.Evaluate([], binding), "Empty validation set refused");
        Reject<ArgumentException>(() => model.Evaluate(Enumerable.Repeat(Pair("check", 1, 1), 65).ToArray(), binding), "Validation resource cap enforced");
        Reject<ArgumentException>(() => model.Evaluate([training[0]], binding), "Training identity/position cannot masquerade as holdout");
        Reject<ArgumentException>(() => model.Evaluate([training[0] with { Name = "renamed" }], binding), "Renaming training point cannot hide reused pixel position");
        Reject<ArgumentException>(() => model.Evaluate([Pair("fit-A", 1, 1)], binding), "Reused training name refused even at a changed point");
        Reject<ArgumentException>(() => model.Evaluate([Pair("same", 1, 1), Pair("same", 2, 1)], binding), "Duplicate check identity refused");
        Reject<ArgumentException>(() => model.Evaluate([Pair("a", 1, 1), Pair("b", 1, 1)], binding), "Duplicated validation pixel position refused");
        Reject<ArgumentException>(() => model.Evaluate([Pair("outside", 7, 5)], binding), "Validation cannot silently expand fitted triangle");
        Reject<ArgumentException>(() => model.Evaluate(Validation(), binding with { GeometryRevision = 2 }), "Validation also checks binding");
        training[0] = Pair("changed-array", 1, 1);
        Check(model.TryMap(new(2, 1), binding, out PlanePoint unchanged) && Near(unchanged, mapped), "Changing caller training array cannot mutate stored calibration");

        Check(ImageCoordinates.RestoreFromCrop(new(1.5, .5), new(2, 1, 4, 3), 8, 6) == new ImagePoint(3.5, 1.5), "ROI local point restored to full-image pixel-center domain");
        ImagePoint parent = ImageCoordinates.RestoreFromCrop(new(.5, .5), new(1, 1, 2, 2), 4, 4);
        Check(ImageCoordinates.RestoreFromCrop(parent, new(2, 1, 4, 4), 8, 6) == new ImagePoint(3.5, 2.5), "Nested crops restore one parent offset at a time");
        Reject<ArgumentException>(() => ImageCoordinates.RestoreFromCrop(new(4, 0), new(2, 1, 4, 3), 8, 6), "Out-of-crop center refused rather than clamped");
        Reject<ArgumentException>(() => ImageCoordinates.RestoreFromCrop(new(0, 0), new(7, 5, 2, 2), 8, 6), "Invalid crop bounds refused");
        Reject<ArgumentException>(() => ImageCoordinates.RestoreFromCrop(new(double.NaN, 0), new(0, 0, 2, 2), 8, 6), "Invalid local point refused");

        Reject<ArgumentNullException>(() => PlanarAffineCalibration.FitThree(null!, Training()), "Missing binding rejected");
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding with { GeometryRevision = 0 }, Training()), "Unknown geometry revision rejected");
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding with { SetupId = "" }, Training()), "Missing setup identity rejected");
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding with { PixelSpace = "" }, Training()), "Missing pixel-space declaration rejected");
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding with { PlaneFrame = "" }, Training()), "Missing plane frame rejected");
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding with { Width = 0 }, Training()), "Invalid source geometry rejected");
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, Training()[..2]), "Underdetermined two-point model rejected");
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, [.. Training(), Pair("fourth", 1, 1)]), "Four points are not silently truncated or advertised as least squares");
        CalibrationPair[] bad = Training(); bad[2] = bad[2] with { Name = "fit-A" };
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Duplicate training names rejected");
        bad = Training(); bad[2] = bad[1] with { Name = "different" };
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Duplicate training coordinates rejected by geometry");
        bad = [Pair("a", 0, 0), Pair("b", 7, 0), Pair("c", 6, 0)];
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Collinear source triangle rejected");
        bad = [Pair("a", 0, 0), Pair("b", 7, 0), Pair("c", 6, 1e-6)];
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Nearly collinear source triangle rejected before unstable inversion");
        bad = [Pair("a", 0, 0), Pair("b", .2, 0), Pair("c", 0, .2)];
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Source triangle below teaching one-pixel edge policy rejected");
        bad = Training(); bad[2] = bad[2] with { Plane = bad[1].Plane };
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Degenerate target plane triangle rejected");
        bad = Training(); bad[2] = bad[2] with { Plane = new(double.PositiveInfinity, 0) };
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Nonfinite physical reference rejected");
        bad = Training(); bad[2] = bad[2] with { Plane = new(1e6 + 1, 0) };
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Teaching physical-coordinate cap enforced");
        bad = Training(); bad[2] = bad[2] with { Image = new(8, 5) };
        Reject<ArgumentException>(() => PlanarAffineCalibration.FitThree(binding, bad), "Out-of-image calibration point rejected");

        bool grid = true; int samples = 0;
        for (int i = 1; i < 10; i++)
            for (int j = 1; j < 10 - i; j++)
            {
                ImagePoint input = new(7d * i / 10, 5d * j / 10);
                grid &= model.TryMap(input, binding, out PlanePoint plane) && Near(plane, Truth(input))
                    && model.TryUnmap(plane, binding, out ImagePoint back) && Near(back, input);
                samples++;
            }
        Check(grid && samples == 36, "36 fixed interior barycentric samples match analytic map and inverse (one supplemental regression)");
        CalibrationPair[] reversed = Training().Reverse().ToArray();
        PlanarAffineCalibration reverseModel = PlanarAffineCalibration.FitThree(binding, reversed);
        Check(reverseModel.TryMap(new(2, 1), binding, out PlanePoint reversedResult) && Near(reversedResult, mapped), "Consistent pair reordering preserves affine map");
        Console.WriteLine($"Calibration checks passed: {count}. Synthetic coordinates only; no lens/hand-eye/motion or accuracy acceptance.");
    }
    public static void Demo()
    {
        PlanarAffineCalibration model = PlanarAffineCalibration.FitThree(Binding, Training());
        _ = model.TryMap(new(2, 1), Binding, out PlanePoint point);
        CalibrationErrorReport report = model.Evaluate(Validation(), Binding);
        Console.WriteLine($"Synthetic pixel (column=2,row=1) -> {point.Xmm:F3}, {point.Ymm:F3} mm in {Binding.PlaneFrame}");
        Console.WriteLine($"Independent check count={report.Count}, RMS={report.RmsMm:F6} mm, max={report.MaxMm:F6} mm");
        Console.WriteLine($"Pixel (7,5) supported={model.TryMap(new(7, 5), Binding, out _)}; changed geometry supported={model.TryMap(new(2, 1), Binding with { GeometryRevision = 2 }, out _)}");
        Console.WriteLine("Synthetic reference offsets are deliberate. Report has no automatic pass or motion permission.");
    }
}
