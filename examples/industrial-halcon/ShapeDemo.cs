using HalconDotNet;

namespace IndustrialHalcon;

public static class ShapeDemo
{
    private const double Start = -Math.PI / 2 - 0.02, Extent = Math.PI + 0.04;
    private static double[] Numbers(HTuple tuple)
    {
        using HTuple real = tuple.TupleReal();
        return BlobReadback.CopyUsedValues(real.DArr, real.Length);
    }
    private static double Scalar(HTuple tuple)
    {
        double[] values = Numbers(tuple);
        if (values.Length != 1) throw new InvalidOperationException("Expected scalar output.");
        return values[0];
    }
    private static HObject Image(ShapeScene scene)
    {
        int[] pixels = ShapeFixture.Pixels(scene);
        using HTuple rows = new(Enumerable.Range(0, pixels.Length).Select(i => i / 96).ToArray());
        using HTuple columns = new(Enumerable.Range(0, pixels.Length).Select(i => i % 96).ToArray());
        using HTuple gray = new(pixels);
        HOperatorSet.GenImageConst(out HObject image, "byte", 96, 96);
        try { HOperatorSet.SetGrayval(image, rows, columns, gray); return image; }
        catch { image.Dispose(); throw; }
    }
    private static IReadOnlyList<ShapePose> Find(HObject image, HTuple model)
    {
        HTuple? rows = null, columns = null, angles = null, scores = null;
        try
        {
            HOperatorSet.FindShapeModel(image, model, Start, Extent, 0.85, 2, 0.3, "least_squares", 1, 0,
                out rows, out columns, out angles, out scores);
            return ShapeContracts.ReadPoses(Numbers(rows), Numbers(columns), Numbers(angles), Numbers(scores));
        }
        finally { scores?.Dispose(); angles?.Dispose(); columns?.Dispose(); rows?.Dispose(); }
    }
    private static ShapeFollowReport Follow(HObject image, HTuple model, HObject normalizedContours, HTuple referencePose)
    {
        IReadOnlyList<ShapePose> candidates = Find(image, model);
        if (candidates.Count != 1)
            return new(candidates.Count == 0 ? ShapeFollowOutcome.NoMatch : ShapeFollowOutcome.Ambiguous,
                candidates.Count, null, Array.Empty<ImagePixel>(), null, 0);
        ShapePose p = candidates[0];
        HTuple? currentPose = null, inverseReference = null, referenceToCurrent = null;
        HTuple? area = null, row = null, column = null, mean = null, deviation = null, equal = null;
        HObject? overlay = null, roiReference = null, roiCurrent = null, domain = null, clipped = null;
        try
        {
            // H_current maps model-normalized coordinates -> current image.
            HOperatorSet.VectorAngleToRigid(0, 0, 0, p.RawRowPixels, p.RawColumnPixels, p.AngleRadians, out currentPose);
            HOperatorSet.AffineTransContourXld(normalizedContours, out overlay, currentPose);
            // H_current * inverse(H_reference) maps ABSOLUTE reference-image ROI -> current image.
            HOperatorSet.HomMat2dInvert(referencePose, out inverseReference);
            HOperatorSet.HomMat2dCompose(currentPose, inverseReference, out referenceToCurrent);
            PixelRigidSnapshot snapshot = PixelRigidSnapshot.FromValues(Numbers(referenceToCurrent));
            IReadOnlyList<ImagePixel> corners = snapshot.Corners(ShapeFixture.InspectionRoi);
            if (!snapshot.Fits(ShapeFixture.InspectionRoi))
                return new(ShapeFollowOutcome.RoiOutsideImage, 1, p, corners, null, overlay.CountObj());
            PixelRoi r = ShapeFixture.InspectionRoi;
            HOperatorSet.GenRectangle1(out roiReference, r.Row1, r.Column1, r.Row2, r.Column2);
            HOperatorSet.AffineTransRegion(roiReference, out roiCurrent, referenceToCurrent, "nearest_neighbor");
            if (roiCurrent.CountObj() != 1) throw new InvalidOperationException("Expected one transformed region.");
            HOperatorSet.GetDomain(image, out domain);
            HOperatorSet.Intersection(roiCurrent, domain, out clipped);
            if (clipped.CountObj() != 1)
                return new(ShapeFollowOutcome.RoiOutsideImage, 1, p, corners, null, overlay.CountObj());
            HOperatorSet.TestEqualRegion(roiCurrent, clipped, out equal);
            if (Scalar(equal) != 1)
                return new(ShapeFollowOutcome.RoiOutsideImage, 1, p, corners, null, overlay.CountObj());
            HOperatorSet.AreaCenter(roiCurrent, out area, out row, out column);
            HOperatorSet.Intensity(roiCurrent, image, out mean, out deviation);
            FollowedRoi readback = ShapeContracts.ReadRoi(Scalar(area), Scalar(row), Scalar(column), Scalar(mean));
            return new(ShapeFollowOutcome.RoiPrepared, 1, p, corners, readback, overlay.CountObj());
        }
        finally
        {
            equal?.Dispose(); deviation?.Dispose(); mean?.Dispose(); column?.Dispose(); row?.Dispose(); area?.Dispose();
            clipped?.Dispose(); domain?.Dispose(); roiCurrent?.Dispose(); roiReference?.Dispose(); overlay?.Dispose();
            referenceToCurrent?.Dispose(); inverseReference?.Dispose(); currentPose?.Dispose();
        }
    }
    private static void Expected(ShapeFollowReport report, double row, double column, double angle)
    {
        if (report.Outcome != ShapeFollowOutcome.RoiPrepared || report.CandidateCount != 1 ||
            report.Pose is not { } pose || report.Roi is not { } roi || report.OverlayContourObjects < 1 ||
            Math.Abs(pose.AngleRadians - angle) > 0.04 || Math.Abs(roi.RowPixels - row) > 1 ||
            Math.Abs(roi.ColumnPixels - column) > 1 || roi.AreaPixels is < 12 or > 18 || roi.MeanGray < 200)
            throw new InvalidOperationException("Native shape/ROI result differs from the synthetic teaching expectation.");
    }
    public static void Run(bool checkExpected)
    {
        HObject? template = null, modelRoi = null, reduced = null, contours = null;
        HTuple? model = null, referencePose = null;
        try
        {
            template = Image(ShapeScene.Reference);
            PixelRoi r = ShapeFixture.ModelRoi;
            HOperatorSet.GenRectangle1(out modelRoi, r.Row1, r.Column1, r.Row2, r.Column2);
            HOperatorSet.ReduceDomain(template, modelRoi, out reduced);
            HOperatorSet.CreateShapeModel(reduced, 1, Start, Extent, Math.PI / 180, "none", "use_polarity", 30, 10, out model);
            HOperatorSet.SetShapeModelOrigin(model, 0, 0);
            HOperatorSet.GetShapeModelContours(out contours, model, 1);
            IReadOnlyList<ShapePose> reference = Find(template, model);
            if (reference.Count != 1) throw new InvalidOperationException("Reference self-match must return exactly one candidate.");
            ShapePose p = reference[0];
            HOperatorSet.VectorAngleToRigid(0, 0, 0, p.RawRowPixels, p.RawColumnPixels, p.AngleRadians, out referencePose);
            using HObject quarterTurn = Image(ShapeScene.QuarterTurn);
            ShapeFollowReport result = Follow(quarterTurn, model, contours, referencePose);
            Console.WriteLine($"Native shape outcome={result.Outcome}, candidates={result.CandidateCount}, contour objects={result.OverlayContourObjects}; not product acceptance.");
            if (result.Roi is { } roi) Console.WriteLine($"Followed ROI: area={roi.AreaPixels}px, row={roi.RowPixels}, column={roi.ColumnPixels}, mean gray={roi.MeanGray}");
            if (!checkExpected) return;
            Expected(result, 55, 53, Math.PI / 2);
            Expected(Follow(template, model, contours, referencePose), 47, 44, 0);
            using HObject translation = Image(ShapeScene.Translation);
            Expected(Follow(translation, model, contours, referencePose), 51, 50, 0);
            using HObject blank = Image(ShapeScene.Blank);
            ShapeFollowReport absent = Follow(blank, model, contours, referencePose);
            if (absent.Outcome != ShapeFollowOutcome.NoMatch || absent.Roi is not null)
                throw new InvalidOperationException("Blank image must not reuse an old ROI.");
            using HObject doubleImage = Image(ShapeScene.Double);
            ShapeFollowReport ambiguous = Follow(doubleImage, model, contours, referencePose);
            if (ambiguous.Outcome != ShapeFollowOutcome.Ambiguous || ambiguous.Roi is not null)
                throw new InvalidOperationException("Two candidates must not auto-select one for ROI follow.");
            Console.WriteLine("Five actual HALCON shape/ROI fixture cases passed. No arbitrary-angle accuracy, safety or production acceptance.");
        }
        finally
        {
            referencePose?.Dispose(); contours?.Dispose(); model?.Dispose();
            reduced?.Dispose(); modelRoi?.Dispose(); template?.Dispose();
        }
    }
}
