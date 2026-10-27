using IndustrialOpenCvSharp;
using OpenCvSharp;

namespace IndustrialOpenCvSharpWpf;

public static class PipelineNativeChecks
{
    public static async Task Run()
    {
        int count = 0;
        void Check(bool v, string label) { if (!v) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        Guid owner = Guid.NewGuid(); FrameSelection bright = FrameFactory.Create(0);
        MeasurementJob Job(ulong n, int image = 0) => new(owner, n, Guid.NewGuid(), FrameFactory.Create(image));
        async Task Finish(MeasurementPipeline p) { p.Complete(); await p.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
        MeasurementPipeline pipeline = new(owner, 128, 96, bright.Recipe, "stripe-v1", 2);
        try
        {
            MeasurementJob a = Job(1), b = Job(2, 2), c = Job(3, 3);
            PipelineSubmission first = pipeline.TrySubmit(a), second = pipeline.TrySubmit(b);
            MeasurementTerminal[] r = await Task.WhenAll(first.Completion!, second.Completion!).WaitAsync(TimeSpan.FromSeconds(10));
            Check(r[0].Stage == PipelineStage.Completed && r[0].Output!.Report.WidthPixels is { } width && Math.Abs(width - 32) < 1e-8, "Actual bounded OpenCV measurement yields32px");
            Check(r[1].Stage == PipelineStage.Completed && r[1].Output!.Report.Outcome == StripeOutcome.Ambiguous && r[1].Output!.Report.WidthPixels is null, "Actual multi-stripe output retained as normal ambiguous outcome");
            byte[] pixels = new byte[128 * 96]; r[0].Output!.Bitmap.CopyPixels(pixels, 128, 0);
            Check(r[0].Output!.Bitmap.IsFrozen && pixels.SequenceEqual(a.Selection.Image.CopyPixels()), "Retained frozen bitmap valid after native call and Mat release");
            Check(pipeline.TrySubmit(c).Admission == PipelineAdmission.Full && pipeline.Snapshot.Unacknowledged == 2, "Actual completed native calls do not bypass result backpressure");
            Check(!r[0].MatchesDisplay(c, pipeline.Session) && pipeline.Acknowledge(r[0]), "Old native result handled even when another image is displayed");
            MeasurementTerminal blank = await pipeline.TrySubmit(c).Completion!.WaitAsync(TimeSpan.FromSeconds(10));
            Check(blank is { Stage: PipelineStage.Completed, Output.Report.Outcome: StripeOutcome.NoEdges } && blank.Output.Report.WidthPixels is null && blank.MatchesDisplay(c, pipeline.Session), "Actual blank frame matches preview but has no width");
            await Finish(pipeline);
            Check(pipeline.Snapshot.Unacknowledged == 2 && pipeline.Acknowledge(r[1]) && pipeline.Acknowledge(blank), "Actual outputs survive drain until exact acknowledgements");
        }
        finally { await Finish(pipeline); }
        Mat? failedMat = null; int attempts = 0;
        MeasurementPipeline fault = new(owner, 128, 96, bright.Recipe, "stripe-v1", 1, source =>
        {
            if (++attempts == 1) return NativeFrameRunner.WithOwnedMat<RenderedMeasurement>(source.Image, mat =>
            { failedMat = mat; throw new InvalidOperationException("Synthetic fault inside actual owned Mat scope"); });
            return NativeFrameRunner.Analyze(source);
        });
        try
        {
            MeasurementTerminal failed = await fault.TrySubmit(Job(1)).Completion!.WaitAsync(TimeSpan.FromSeconds(10));
            Check(failed is { Stage: PipelineStage.Failed, Error: nameof(InvalidOperationException), Output: null } && failedMat!.IsDisposed, "Synthetic fault INSIDE actual Mat scope disposes input before Failed publication");
            Check(fault.TrySubmit(Job(2)).Admission == PipelineAdmission.Full && fault.Acknowledge(failed), "Actual-scope failure retains capacity until handled");
            MeasurementTerminal next = await fault.TrySubmit(Job(2)).Completion!.WaitAsync(TimeSpan.FromSeconds(10));
            Check(next.Stage == PipelineStage.Completed && next.Output!.Report.WidthPixels is { } w && Math.Abs(w - 32) < 1e-8 && attempts == 2 && fault.Acknowledge(next), "Next explicit job executes native algorithm, no replay/fallback");
        }
        finally { await Finish(fault); }
        Mat? heldMat = null;
        using ManualResetEventSlim entered = new(false), release = new(false); int calls = 0;
        MeasurementPipeline draining = new(owner, 128, 96, bright.Recipe, "stripe-v1", 2, source => NativeFrameRunner.WithOwnedMat(source.Image, mat =>
        {
            if (++calls == 1) { heldMat = mat; entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); }
            return new RenderedMeasurement(BitmapBridge.FromMat(mat), StripeDemo.Measure(mat, source.Recipe));
        }));
        try
        {
            PipelineSubmission running = draining.TrySubmit(Job(1)); Check(entered.Wait(2000), "Real worker holds actual Mat before close");
            MeasurementJob queued = Job(2); PipelineSubmission waiting = draining.TrySubmit(queued);
            LatestMeasurementPreview preview = new(); preview.Publish(queued); preview.Close(); draining.Complete();
            Check(!draining.Completion.IsCompleted && !heldMat!.IsDisposed && draining.Snapshot is { Closed: true, Running: 1, Queued: 1 }, "Closing preview/admission does not discard queued job or dispose in-use Mat");
            release.Set(); MeasurementTerminal[] r = await Task.WhenAll(running.Completion!, waiting.Completion!).WaitAsync(TimeSpan.FromSeconds(10));
            await Finish(draining);
            Check(heldMat!.IsDisposed && calls == 2 && r.All(v => v.Stage == PipelineStage.Completed) && draining.Snapshot.Unacknowledged == 2, "Actual drain processes both accepted jobs and retains both terminals");
            Check(r.All(draining.Acknowledge) && draining.Snapshot.Retained == 0 && preview.Take() is null, "Preview closure is separate from explicit measurement acknowledgements");
        }
        finally { release.Set(); await Finish(draining); }
        int rounds = 0, disposed = 0, active = 0, peak = 0;
        MeasurementPipeline smoke = new(owner, 128, 96, bright.Recipe, "stripe-v1", 1, source =>
        {
            active++; peak = Math.Max(peak, active); Mat? input = null;
            try
            {
                RenderedMeasurement output = NativeFrameRunner.WithOwnedMat(source.Image, mat =>
                { input = mat; return new RenderedMeasurement(BitmapBridge.FromMat(mat), StripeDemo.Measure(mat, source.Recipe)); });
                rounds++; if (input!.IsDisposed) disposed++; return output;
            }
            finally { active--; }
        });
        try
        {
            Console.WriteLine($"Resource sample before32: {ProcessResourceSample.Read(smoke)}");
            bool all = true;
            for (ulong n = 1; n <= 32; n++)
            {
                int image = n % 3 == 1 ? 0 : n % 3 == 2 ? 2 : 3;
                PipelineSubmission offer = smoke.TrySubmit(Job(n, image));
                if (offer.Completion is null) throw new InvalidOperationException("Bounded sequential sample not admitted.");
                MeasurementTerminal terminal = await offer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
                all &= offer.Admission == PipelineAdmission.Accepted && terminal.Stage == PipelineStage.Completed && smoke.Snapshot is { Retained: 1, Running: 0, Unacknowledged: 1 };
                all &= smoke.Acknowledge(terminal) && smoke.Snapshot.Retained == 0;
            }
            await Finish(smoke);
            Check(all && rounds == 32 && disposed == 32 && peak == 1 && active == 0, "32 native jobs: each input Mat disposed, one backend scope at a time, bounded terminal ledger; NOT long-run performance acceptance");
            Check(smoke.Snapshot is { Accepted: 32, Completed: 32, Failed: 0, Acknowledged: 32, Retained: 0 }, "32 accepted job outcomes all explicitly handled");
            Console.WriteLine($"Resource sample after32: {ProcessResourceSample.Read(smoke)}");
        }
        finally { await Finish(smoke); }
        Console.WriteLine($"Native bounded-pipeline checks passed: {count}. Actual OpenCV/bitmap and finite32-job smoke; memory samples are not leak, throughput, hardware or production acceptance.");
    }
}
