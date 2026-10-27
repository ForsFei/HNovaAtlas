namespace IndustrialVision;

public static class DisplayChecks
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
    public static async Task Run()
    {
        count = 0;
        Check(UniformImageViewport.TryCreate(8, 6, 1000, 600, out UniformImageViewport viewport), "Uniform viewport created");
        Check(Near(viewport.Scale, 100) && viewport.ImageBounds == new DisplayRect(100, 0, 800, 600), "Wide viewport has correct side letterboxes");
        Check(viewport.TryToDisplay(new(0, 0), out DisplayPoint first) && first == new DisplayPoint(150, 50), "First pixel center includes half-pixel offset");
        Check(viewport.TryToDisplay(new(7, 5), out DisplayPoint last) && last == new DisplayPoint(850, 550), "Last center stays inside image footprint");
        Check(viewport.TryToDisplay(new(1.5, 2.25), out DisplayPoint subpixel) && subpixel == new DisplayPoint(300, 275), "Subpixel center mapping remains continuous");
        Check(viewport.TryPickPixel(first, out PixelHit hit) && hit == new PixelHit(0, 0), "Display center picks original integer pixel");
        Check(viewport.TryPickPixel(new(100, 0), out hit) && hit == new PixelHit(0, 0), "Image footprint includes its top-left edge for picking");
        Check(!viewport.TryPickPixel(new(99.9, 200), out _) && !viewport.TryPickPixel(new(950, 200), out _), "Letterboxes are not clamped into valid pixels");
        Check(!viewport.TryPickPixel(new(900, 100), out _) && !viewport.TryPickPixel(new(200, 600), out _), "Right and bottom image edges are exclusive");
        Check(!viewport.TryPickPixel(new(double.NaN, 1), out _) && !viewport.TryPickPixel(new(1, double.PositiveInfinity), out _), "Nonfinite display positions refused");
        Check(!viewport.TryToDisplay(new(-.1, 0), out _) && !viewport.TryToDisplay(new(8, 0), out _), "Center-domain forward mapping does not accept footprint edges as centers");
        Check(viewport.TryRoiBounds(new(1, 1, 2, 2), out DisplayRect roi) && roi == new DisplayRect(200, 100, 200, 200), "ROI rectangle uses pixel edges, not center coordinates");
        Check(viewport.TryRoiBounds(new(0, 0, 8, 6), out roi) && roi == viewport.ImageBounds, "Full ROI equals image footprint");
        Check(!viewport.TryRoiBounds(new(7, 5, 2, 2), out _) && !viewport.TryRoiBounds(new(0, 0, 0, 1), out _), "Invalid ROI refused");
        Check(UniformImageViewport.TryCreate(8, 6, 400, 600, out UniformImageViewport tall)
            && tall.ImageBounds == new DisplayRect(0, 150, 400, 300), "Tall viewport has correct top and bottom letterboxes");
        Check(!tall.TryPickPixel(new(200, 149.9), out _) && tall.TryPickPixel(new(25, 175), out hit) && hit == new PixelHit(0, 0), "Tall-layout pick uses actual drawn rectangle");
        Check(UniformImageViewport.TryCreate(8, 6, 4, 3, out UniformImageViewport small)
            && small.TryToDisplay(new(0, 0), out DisplayPoint tiny) && tiny == new DisplayPoint(.25, .25), "Downscaled image uses same center convention");
        Check(!UniformImageViewport.TryCreate(8, 6, 0, 600, out _) && !UniformImageViewport.TryCreate(8, 6, 100, -1, out _), "Unlaid-out or negative viewport refused");
        Check(!UniformImageViewport.TryCreate(0, 6, 100, 100, out _) && !UniformImageViewport.TryCreate(2000, 2000, 100, 100, out _), "Invalid image dimensions/resource cap refused");
        Check(!UniformImageViewport.TryCreate(8, 6, double.NaN, 100, out _) && !UniformImageViewport.TryCreate(8, 6, 1e6 + 1, 100, out _), "Nonfinite or over-policy viewport refused");
        bool allCenters = true;
        for (int row = 0; row < 6; row++) for (int col = 0; col < 8; col++)
            allCenters &= viewport.TryToDisplay(new(col, row), out DisplayPoint dp) && viewport.TryPickPixel(dp, out PixelHit picked) && picked == new PixelHit(col, row);
        Check(allCenters, "All 48 fixed pixel centers round-trip in a letterboxed viewport (one regression)");

        Guid owner = Guid.NewGuid(), camera = Guid.NewGuid();
        ProcessingRecipe recipe = new("roi-statistics-v1", 1, new(1, 1, 2, 2), 200);
        using OfflineFrameSource source = new([PlainPgm.LoadFixture()]); source.Open();
        CaptureResult Capture(uint number)
        {
            using FrameLease lease = source.Acquire();
            return new(new(new(owner, number), $"synthetic-{number}", 1), new(lease.CopyToOwned(), camera, 1, number, number, true, true));
        }
        CaptureResult a = Capture(1), b = Capture(2);
        InspectionPipeline pipeline = new(owner, 8, 6, recipe);
        try
        {
            FrameDisplayState state = new(owner, pipeline.Session, recipe);
            Check(state.Current is null && state.Result is null && state.OverlayRegion is null, "Display starts without stale frame/result/overlay");
            InspectionResult ra = await pipeline.TrySubmit(a).Result!.WaitAsync(TimeSpan.FromSeconds(5));
            InspectionResult rb = await pipeline.TrySubmit(b).Result!.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!state.TryAttach(ra), "Result arriving with no displayed frame is ignored");
            Check(state.Show(a) && state.Current == a && state.Result is null, "Explicit image show clears previous result");
            Check(!state.TryAttach(rb) && state.OverlayRegion is null, "Future or other frame cannot attach to current pixels");
            Check(!state.TryAttach(ra with { PipelineSession = Guid.NewGuid() }), "Foreign pipeline result refused");
            Check(!state.TryAttach(ra with { Request = ra.Request with { Workpiece = "different" } }), "Workpiece mismatch refused, even with same frame");
            Check(!state.TryAttach(ra with { Request = ra.Request with { Key = new(owner, 99) } }), "Request sequence mismatch refused");
            Check(!state.TryAttach(ra with { Frame = new(Guid.NewGuid(), ra.Frame.Number) }), "Same numeric frame from another source session refused");
            Check(!state.TryAttach(ra with { CameraEpoch = Guid.NewGuid() }), "Camera execution epoch mismatch refused");
            Check(!state.TryAttach(ra with { TriggerSequence = 99 }), "Original trigger mismatch refused");
            Check(!state.TryAttach(ra with { Recipe = recipe with { Version = "new-version" } }), "Different algorithm recipe version refused");
            Check(!state.TryAttach(ra with { Recipe = recipe with { Region = new(0, 0, 2, 2) } }), "Different ROI recipe refused");
            Check(!state.TryAttach(ra with { Request = ra.Request with { ProfileRevision = 2 } }), "Acquisition profile mismatch refused");
            Check(!state.TryAttach(ra with { Measurement = null }), "Completed terminal without measurement cannot draw");
            Check(!state.TryAttach(ra with { Measurement = ra.Measurement! with { Region = new(0, 0, 2, 2) } }), "Measurement ROI must agree with frozen recipe");
            Check(!state.TryAttach(ra with { Measurement = ra.Measurement! with { Mean = double.NaN } }), "Nonfinite measurement refused");
            Check(!state.TryAttach(ra with { Stage = ProcessingStage.Processing }), "Intermediate processing state is not a drawable terminal");
            Check(state.TryAttach(ra) && state.OverlayRegion == recipe.Region && state.Result == ra, "Matching completed terminal permits only its ROI overlay");
            Check(state.TryAttach(ra), "Same terminal object is idempotent");
            Check(!state.TryAttach(ra with { Measurement = ra.Measurement! with { Mean = 0 } }) && state.Result == ra, "Conflicting terminal cannot overwrite accepted display result");
            Check(state.Show(b) && state.Result is null && state.OverlayRegion is null, "Changing pixels immediately removes old result and graphics");
            Check(!state.TryAttach(ra) && state.Current == b && state.OverlayRegion is null, "Late old result leaves new preview untouched");
            Check(state.TryAttach(rb) && state.Result?.Request.Key == b.Request.Key, "New preview accepts its own result");
            _ = state.Show(a);
            InspectionResult failure = ra with { Stage = ProcessingStage.Failed, Measurement = null, ErrorCode = "SyntheticFailure" };
            Check(state.TryAttach(failure) && state.Result == failure && state.OverlayRegion is null, "Matching failure remains visible as status but never as a good ROI");
            state.Clear();
            Check(state.Current is null && state.Result is null && !state.TryAttach(rb), "Clear also invalidates late display offers");
            Check(!state.Show(a with { Evidence = a.Evidence with { Complete = false } }), "Incomplete display input refused");
            Check(!state.Show(a with { Evidence = a.Evidence with { IdentityQualified = false } }), "Unqualified display input refused");
            Check(!state.Show(a with { Request = a.Request with { Key = new(Guid.NewGuid(), 1) } }), "Foreign capture owner refused");
            Check(!state.Show(a with { Evidence = a.Evidence with { ProfileRevision = 2 } }), "Wrong display profile refused");
            Check(!state.Show(a with { Evidence = a.Evidence with { Frame = new(new(Guid.Empty, 1), a.Evidence.Frame.Image) } }), "Unstamped image cannot enter display state");
            Check(!state.Show(a with { Evidence = a.Evidence with { Frame = new(a.Evidence.Frame.Stamp, Mono8Image.FromPacked(1, 1, [0])) } }), "Image too small for frozen ROI refused");
            Check(!state.Show(null!) && !state.TryAttach(null!), "Null offers do not mutate display state");
            Check(pipeline.Acknowledge(ra) && pipeline.Acknowledge(rb), "Local result handoff is independent of whether UI displayed the result");
        }
        finally { pipeline.Complete(); await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(5)); }
        Console.WriteLine($"Display checks passed: {count}. Pure geometry/metadata only; no WPF window, camera or product verdict.");
    }
}
