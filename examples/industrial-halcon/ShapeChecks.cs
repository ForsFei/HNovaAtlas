namespace IndustrialHalcon;

public static class ShapeChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine($"PASS {++count}: {label}");
        }
        void Reject(Action action, string label)
        {
            try { action(); } catch (ArgumentException) { Check(true, label); return; }
            throw new InvalidOperationException($"Expected invalid input: {label}");
        }
        PixelRigidSnapshot Matrix(double[] values) => PixelRigidSnapshot.FromValues(values);
        PixelRigidSnapshot identity = Matrix([1, 0, 0, 0, 1, 0]);
        Check(identity.Map(47, 44) == new ImagePixel(47, 44), "Identity preserves pixel centers");
        PixelRigidSnapshot translated = Matrix([1, 0, 4, 0, 1, 6]);
        Check(translated.Map(47, 44) == new ImagePixel(51, 50), "Translation maps absolute reference ROI center");
        PixelRigidSnapshot rotated = Matrix([0, -1, 100, 1, 0, 6]);
        Check(rotated.Map(47, 44) == new ImagePixel(55, 53), "90-degree corner-coordinate transform includes half-pixel convention");
        Check(rotated.Map(46, 42) == new ImagePixel(57, 52), "Quarter-turn maps upper-left center to lower-left, not a translation only");
        Check(rotated.Map(48, 46) == new ImagePixel(53, 54), "Opposite corner follows the same rotation");
        Check(rotated.Map(0, 0) == new ImagePixel(99, 6), "Pixel-center mapping differs from naively applying matrix to (0,0)");
        double[] mutable = [1, 0, 4, 0, 1, 6];
        PixelRigidSnapshot copied = Matrix(mutable); mutable[2] = 900;
        Check(copied.Map(47, 44).Row == 51, "Snapshot does not retain caller coefficient storage");
        Reject(() => Matrix([1, 0, 0, 0, 1]), "Five coefficients refused");
        Reject(() => Matrix([1, 0, 0, 0, 1, 0, 0, 0, 1]), "Full projective tuple not accepted by six-coefficient adapter");
        Reject(() => Matrix([1, 0, double.NaN, 0, 1, 0]), "Nonfinite matrix refused");
        Reject(() => Matrix([1, 0, 10_001, 0, 1, 0]), "Teaching matrix bound enforced");
        Reject(() => Matrix([2, 0, 0, 0, 2, 0]), "Scaling refused in rigid-only snapshot");
        Reject(() => Matrix([1, .1, 0, 0, 1, 0]), "Shear refused");
        Reject(() => Matrix([-1, 0, 0, 0, 1, 0]), "Reflection refused even when columns have unit norm");
        Reject(() => Matrix([0, 0, 0, 0, 0, 0]), "Singular matrix refused");
        Reject(() => Matrix(null!), "Null coefficient array refused");
        Reject(() => identity.Map(double.PositiveInfinity, 0), "Nonfinite point refused");
        Reject(() => identity.Map(0, 10_001), "Unbounded input point refused");
        PixelRoi roi = ShapeFixture.InspectionRoi;
        IReadOnlyList<ImagePixel> corners = rotated.Corners(roi);
        Check(corners.Count == 4 && corners[0] == new ImagePixel(57, 52) && corners[2] == new ImagePixel(53, 54), "Four ordered corner centers retained, not axis-aligned bounding-box replacements");
        try { ((IList<ImagePixel>)corners)[0] = new(0, 0); throw new InvalidOperationException("Expected read-only corners"); }
        catch (NotSupportedException) { Check(true, "Published corners are read-only"); }
        Check(rotated.Fits(roi) && identity.Fits(new(0, 0, 95, 95)), "In-image corner-center guard accepts fixed endpoints");
        Check(!Matrix([1, 0, -47, 0, 1, 0]).Fits(roi), "Partly negative ROI refused, not clamped");
        Check(!Matrix([1, 0, 49, 0, 1, 0]).Fits(roi), "Partly beyond-bottom ROI refused");
        Reject(() => identity.Corners(new(48, 42, 46, 46)), "Invalid reference rectangle refused before mapping");
        IReadOnlyList<ShapePose> Read(double[] r, double[] c, double[] a, double[] s) => ShapeContracts.ReadPoses(r, c, a, s);
        Check(Read([], [], [], []).Count == 0, "Four empty tuple snapshots yield zero candidates");
        double[] rr = [44], cc = [48], aa = [Math.PI / 2], ss = [.99];
        IReadOnlyList<ShapePose> poses = Read(rr, cc, aa, ss); rr[0] = cc[0] = aa[0] = ss[0] = 0;
        Check(poses[0] == new ShapePose(44, 48, Math.PI / 2, .99), "Pose snapshots retain radians and independent raw coordinates");
        Check(Read([43, 43], [26, 70], [0, 0], [1, .95]).Count == 2, "Two candidates retained for ambiguity branch, not auto-picked");
        Reject(() => Read([1], [], [0], [1]), "Coordinate length mismatch refused");
        Reject(() => Read([1], [1], [], [1]), "Angle length mismatch refused");
        Reject(() => Read([1], [1], [0], []), "Empty scores do not erase populated pose arrays");
        Reject(() => Read([1, 2, 3], [1, 2, 3], [0, 0, 0], [1, 1, 1]), "Two-result teaching cap enforced");
        Reject(() => Read([double.NaN], [1], [0], [1]), "Nonfinite raw row refused");
        Reject(() => Read([1], [96], [0], [1]), "Raw coordinate outside fixed fixture refused");
        Reject(() => Read([1], [1], [2], [1]), "Angle beyond fixed fixture envelope refused");
        Reject(() => Read([1], [1], [double.NaN], [1]), "Nonfinite angle refused");
        Reject(() => Read([1], [1], [0], [.84]), "Below-threshold shape score refused");
        Reject(() => Read([1], [1], [0], [1.01]), "Out-of-range score refused without clamping");
        Reject(() => Read([1], [1], [0], [double.NaN]), "Nonfinite score refused");
        Reject(() => Read(null!, [], [], []), "Missing tuple snapshot refused");
        Check(ShapeContracts.ReadRoi(15, 55, 53, 220) == new FollowedRoi(15, 55, 53, 220), "ROI statistics remain pixels/gray, not product OK");
        Reject(() => ShapeContracts.ReadRoi(0, 0, 0, 0), "Empty region cannot become a measured origin target");
        Reject(() => ShapeContracts.ReadRoi(15.5, 55, 53, 220), "Fractional raster pixel count refused");
        Reject(() => ShapeContracts.ReadRoi(15, 55, 53, 256), "Mean outside byte range refused");
        Reject(() => ShapeContracts.ReadRoi(15, -1, 53, 220), "Invalid ROI center refused");
        Reject(() => ShapeContracts.ReadRoi(15, 55, 53, double.NaN), "Nonfinite ROI mean refused");
        int[] reference = ShapeFixture.Pixels(ShapeScene.Reference), quarter = ShapeFixture.Pixels(ShapeScene.QuarterTurn);
        Check(reference.Length == 9216 && reference.Count(v => v == 220) == 532, "Original F mark has 532 bright pixels in 96x96 image");
        Check(quarter.Count(v => v == 220) == 532 && quarter.All(v => v is 16 or 220), "Quarter-turn fixture preserves bright count and gray values");
        bool same = true;
        for (int r = 28; r <= 59; r++) for (int c = 28; c <= 59; c++)
            if (reference[r * 96 + c] == 220) same &= quarter[(99 - c) * 96 + r + 6] == 220;
        Check(same, "Exact integer quarter-turn placement verified without HALCON matching");
        Check(ShapeFixture.Pixels(ShapeScene.Double).Count(v => v == 220) == 1064, "Double scene contains two non-overlapping marks");
        Check(ShapeFixture.Pixels(ShapeScene.Blank).All(v => v == 16), "Blank input is actual uniform pixels, not a forced no-match result");
        bool roiBright = true;
        for (int r = 46; r <= 48; r++) for (int c = 42; c <= 46; c++)
            roiBright &= reference[r * 96 + c] == 220 && quarter[(99 - c) * 96 + r + 6] == 220;
        Check(roi.Area == 15 && roiBright, "Fifteen inspection pixels lie in the intended arm before and after quarter-turn");
        Check(ShapeFixture.ModelRoi.Area == 1600, "Model domain is distinct from inspection ROI and bright mark area");
        Reject(() => ShapeFixture.Pixels((ShapeScene)99), "Unknown fixture scene refused");
        Console.WriteLine($"Shape managed checks passed: {count}. Numeric geometry/fixture/output contracts only; no shape search, raster transform or HALCON native call.");
    }
}
