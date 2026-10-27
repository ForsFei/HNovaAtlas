using IndustrialOpenCvSharp;
using IndustrialVision;

namespace IndustrialOpenCvSharpWpf;

public static class PipelineChecks
{
    public static async Task Run()
    {
        int count = 0;
        void Check(bool v, string label) { if (!v) throw new InvalidOperationException(label); Console.WriteLine($"PASS {++count}: {label}"); }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
        Guid owner = Guid.NewGuid(); FrameSelection frame = FrameFactory.Create(0);
        MeasurementJob Job(ulong n, int image = 0) => new(owner, n, Guid.NewGuid(), FrameFactory.Create(image));
        MeasurementPipeline New(int capacity = 2, Func<FrameSelection, RenderedMeasurement>? backend = null) =>
            new(owner, 128, 96, frame.Recipe, "stripe-v1", capacity, backend ?? SessionChecks.Fake);
        async Task Finish(MeasurementPipeline p) { p.Complete(); await p.Completion.WaitAsync(TimeSpan.FromSeconds(5)); }
        Reject(() => _ = new MeasurementPipeline(Guid.Empty, 128, 96, frame.Recipe, "v1"), "Empty producer refused before starting worker");
        Reject(() => _ = New(0), "Zero capacity refused");
        Reject(() => _ = New(9), "Teaching capacity upper bound8");
        Reject(() => _ = new MeasurementPipeline(owner, 128, 96, frame.Recipe, ""), "Explicit version required");
        Reject(() => _ = new MeasurementPipeline(owner, 32, 32, frame.Recipe, "v1"), "Recipe must fit fixed image dimensions");
        using ManualResetEventSlim entered = new(false), release = new(false);
        int calls = 0, active = 0, peak = 0, workerThread = 0, uiThread = Environment.CurrentManagedThreadId;
        MeasurementPipeline pipeline = New(2, source =>
        {
            int now = Interlocked.Increment(ref active); peak = Math.Max(peak, now); workerThread = Environment.CurrentManagedThreadId;
            try
            {
                if (Interlocked.Increment(ref calls) == 1) { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); }
                return SessionChecks.Fake(source);
            }
            finally { Interlocked.Decrement(ref active); }
        });
        MeasurementJob a = Job(1), b = Job(2, 2), c = Job(3, 3);
        try
        {
            Check(pipeline.Snapshot is { Retained: 0, Capacity: 2, Closed: false } && pipeline.Session != Guid.Empty, "Empty bounded ledger and new pipeline session");
            Check(pipeline.TrySubmit(null!).Admission == PipelineAdmission.Invalid, "Null job refused without creating work");
            Check(!pipeline.Acknowledge(null!) && !pipeline.Acknowledge(new(pipeline.Session, "stripe-v1", null!, PipelineStage.Completed, null, null)), "Null/malformed receipt cannot acknowledge");
            Check(pipeline.TrySubmit(a with { Producer = Guid.NewGuid() }).Admission == PipelineAdmission.Invalid, "Wrong producer refused");
            Check(pipeline.TrySubmit(a with { Number = 0 }).Admission == PipelineAdmission.Invalid, "Zero request number refused");
            Check(pipeline.TrySubmit(a with { FrameId = Guid.Empty }).Admission == PipelineAdmission.Invalid, "Missing local frame identity refused");
            Check(pipeline.TrySubmit(a with { Selection = FrameFactory.Create(1) }).Admission == PipelineAdmission.Invalid, "Different recipe cannot reuse fixed pipeline version");
            FrameSelection wrongSize = frame with { Image = Mono8Image.FromPacked(128, 128, new byte[128 * 128]) };
            Check(pipeline.TrySubmit(a with { Selection = wrongSize }).Admission == PipelineAdmission.Invalid, "Different image envelope refused");
            PipelineSubmission first = pipeline.TrySubmit(a);
            Check(first.Admission == PipelineAdmission.Accepted && entered.Wait(2000), "First request running under deterministic gate");
            Check(workerThread != uiThread, "Single consumer backend is off UI thread");
            PipelineSubmission second = pipeline.TrySubmit(b);
            Check(second.Admission == PipelineAdmission.Accepted && pipeline.Snapshot is { Running: 1, Queued: 1, Retained: 2 }, "Capacity accounts for one running and one queued job");
            Check(pipeline.Read(1)?.Stage == PipelineStage.Running && pipeline.Read(2)?.Stage == PipelineStage.Queued && pipeline.Read(99) is null, "Read reports local bounded stages");
            PipelineSubmission duplicate = pipeline.TrySubmit(a);
            Check(duplicate.Admission == PipelineAdmission.AlreadyTracked && ReferenceEquals(duplicate.Completion, first.Completion), "Exact duplicate returns SAME completion without reexecution");
            Check(pipeline.TrySubmit(a with { }).Admission == PipelineAdmission.Conflict, "New object with same number is conflict, even equal fields");
            PipelineSubmission full = pipeline.TrySubmit(c);
            Check(full.Admission == PipelineAdmission.Full && full.Completion is null && pipeline.Read(3) is null, "Full means not accepted, no task/ledger entry");
            LatestMeasurementPreview preview = new();
            Check(preview.Publish(a) == PreviewAdmission.Published && preview.Publish(b) == PreviewAdmission.Replaced, "Preview replaces its own pending image");
            Check(preview.Publish(c) == PreviewAdmission.Replaced && ReferenceEquals(preview.Take(), c), "Unaccepted measurement frame may independently become preview");
            Check(preview.Take() is null && pipeline.Snapshot.Retained == 2, "Taking preview does NOT release measurement capacity");
            release.Set();
            MeasurementTerminal[] results = await Task.WhenAll(first.Completion!, second.Completion!).WaitAsync(TimeSpan.FromSeconds(5));
            Check(results.All(r => r.Stage == PipelineStage.Completed && r.Error is null) && results[0].Output!.Report.WidthPixels == 32,
                "Managed test-double normal measurements are retained terminals");
            Check(results[1].Output!.Report.Outcome == StripeOutcome.Ambiguous && results[1].Output!.Report.WidthPixels is null, "Ambiguous is completed computation, not failed backend or width0");
            Check(pipeline.Snapshot is { Queued: 0, Running: 0, Unacknowledged: 2, Retained: 2 } && pipeline.TrySubmit(c).Admission == PipelineAdmission.Full,
                "Empty channel is STILL full while terminals unacknowledged");
            Check(pipeline.TrySubmit(a).Admission == PipelineAdmission.AlreadyTracked && calls == 2 && peak == 1, "Completed duplicate does not run backend again; one active consumer");
            Check(!results[0].MatchesDisplay(c, pipeline.Session) && !results[0].MatchesDisplay(a with { }, pipeline.Session), "Same dimensions/equal fields cannot attach old result to another display request");
            Check(results[0].MatchesDisplay(a, pipeline.Session) && !results[0].MatchesDisplay(a, Guid.NewGuid()), "Display matching includes local session and exact request identity");
            Check(!pipeline.Acknowledge(results[0] with { }) && !pipeline.Acknowledge(results[0] with { Session = Guid.NewGuid() }), "Forged/copied or foreign-session receipt cannot acknowledge");
            Check(pipeline.Acknowledge(results[0]) && pipeline.Snapshot is { Retained: 1, Acknowledged: 1 }, "Exact terminal acknowledgement releases one ledger slot");
            Check(!pipeline.Acknowledge(results[0]) && pipeline.TrySubmit(a).Admission == PipelineAdmission.Stale, "No double acknowledgement or replay of acknowledged request number");
            PipelineSubmission retried = pipeline.TrySubmit(c);
            Check(retried.Admission == PipelineAdmission.Accepted, "Explicit retry of previously unaccepted number allowed; Full did not advance high-water");
            MeasurementTerminal third = await retried.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
            Check(third.Output!.Report.Outcome == StripeOutcome.NoEdges && third.MatchesDisplay(c, pipeline.Session), "Matching NoEdges terminal has no fabricated width");
            await Finish(pipeline);
            Check(pipeline.Snapshot is { Closed: true, Unacknowledged: 2, Retained: 2 }, "Drain finishes worker but preserves unacknowledged outcomes");
            Check(pipeline.TrySubmit(Job(4)).Admission == PipelineAdmission.Closed && pipeline.Read(2)?.Terminal == results[1], "Closed refuses new input, retained result still readable");
            Check(pipeline.Acknowledge(results[1]) && pipeline.Acknowledge(third) && pipeline.Read(3) is null, "Results can be handled/acknowledged AFTER drain");
            Check(pipeline.Snapshot is { Retained: 0, Accepted: 3, Completed: 3, Failed: 0, Acknowledged: 3 }, "Terminal accounting closes without losing accepted jobs");
            Check(preview.Publish(a) == PreviewAdmission.Published && ReferenceEquals(preview.Take(), a), "Preview follows publication order, not monotonic capture/request number");
            preview.Publish(b); preview.Close(); preview.Close();
            PreviewSnapshot ps = preview.Snapshot;
            Check(ps is { Pending: false, Closed: true, Published: 5, Replaced: 2, Taken: 2, ClearedOnClose: 1 }, "Preview slot accounting distinguishes replacement/take/close clear");
            Check(preview.Publish(c) == PreviewAdmission.Closed && preview.Take() is null, "Closed preview refuses publication and holds no image");
            Check(new LatestMeasurementPreview().Publish(a with { FrameId = Guid.Empty }) == PreviewAdmission.Invalid, "Malformed preview input refused");
            Check(new LatestMeasurementPreview().Publish(null!) == PreviewAdmission.Invalid, "Null preview input refused");
        }
        finally { release.Set(); await Finish(pipeline); }
        MeasurementPipeline failure = New(1, _ => throw new DllNotFoundException("Synthetic only, not an actual OpenCV failure"));
        try
        {
            MeasurementTerminal failed = await failure.TrySubmit(Job(1)).Completion!.WaitAsync(TimeSpan.FromSeconds(5));
            Check(failed is { Stage: PipelineStage.Failed, Output: null, Error: nameof(DllNotFoundException) }, "Synthetic backend fault becomes retained Failed, not NoEdges/fallback");
            Check(failure.TrySubmit(Job(2)).Admission == PipelineAdmission.Full && failure.Snapshot.Failed == 1, "Failed terminal also occupies capacity");
            await Finish(failure); Check(failure.Acknowledge(failed) && failure.Snapshot.Retained == 0, "Failure requires explicit handling even after drain");
        }
        finally { await Finish(failure); }
        int malformedCalls = 0;
        MeasurementPipeline malformed = New(1, source =>
        {
            RenderedMeasurement output = SessionChecks.Fake(source);
            return ++malformedCalls == 1 ? output with { Report = output.Report with { WidthPixels = 999 } } : output;
        });
        try
        {
            MeasurementTerminal invalid = await malformed.TrySubmit(Job(1)).Completion!.WaitAsync(TimeSpan.FromSeconds(5));
            Check(invalid is { Stage: PipelineStage.Failed, Error: nameof(ArgumentException), Output: null }, "Existing output validator rejects inconsistent width before publication");
            malformed.Acknowledge(invalid);
            MeasurementTerminal recovered = await malformed.TrySubmit(Job(2)).Completion!.WaitAsync(TimeSpan.FromSeconds(5));
            Check(recovered.Stage == PipelineStage.Completed && malformedCalls == 2 && malformed.Acknowledge(recovered), "Worker survives ordinary backend/report failure, no automatic retry");
        }
        finally { await Finish(malformed); }
        int concurrentCalls = 0; MeasurementPipeline concurrent = New(1, source => { Interlocked.Increment(ref concurrentCalls); return SessionChecks.Fake(source); });
        try
        {
            MeasurementJob same = Job(1);
            PipelineSubmission[] offered = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => concurrent.TrySubmit(same))));
            Check(offered.Count(s => s.Admission == PipelineAdmission.Accepted) == 1 && offered.Count(s => s.Admission == PipelineAdmission.AlreadyTracked) == 31,
                "32 concurrent offers of same object produce one acceptance; not a sustained throughput test");
            MeasurementTerminal terminal = await offered[0].Completion!.WaitAsync(TimeSpan.FromSeconds(5));
            Check(offered.All(s => ReferenceEquals(s.Completion, offered[0].Completion)) && concurrentCalls == 1 && concurrent.Acknowledge(terminal), "Concurrent duplicates share one terminal and one backend invocation");
        }
        finally { await Finish(concurrent); }
        ProcessResourceSample sample = ProcessResourceSample.Read(pipeline);
        Check(sample.ManagedBytes >= 0 && sample.PrivateBytes > 0 && sample.WorkingSetBytes > 0 && sample.Pipeline.Retained == 0, "Resource sample reads current process and ledger; no flat-memory assertion");
        Console.WriteLine($"Bounded WPF/pipeline checks passed: {count}. Actual Channel/bitmap plus explicit managed backend doubles; no OpenCV, visible-window, persistence or performance acceptance.");
    }
}
