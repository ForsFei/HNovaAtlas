namespace IndustrialVision;

public static class PipelineChecks
{
    private static int count;
    private static readonly TimeSpan TestLimit = TimeSpan.FromSeconds(5);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS {++count}: {label}");
    }
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {label}");
    }
    private static ProcessingRecipe Recipe => new("roi-statistics-v1", 1, new(1, 1, 2, 2), 200);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Finish(InspectionPipeline pipeline)
    {
        pipeline.Complete(); await pipeline.Completion.WaitAsync(TestLimit);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly Guid camera = Guid.NewGuid();
        private double time;
        private ulong trigger;
        public OfflineFrameSource Source { get; } = new([PlainPgm.LoadFixture()]);
        public SingleCaptureSession Capture { get; }
        public Fixture()
        {
            Source.Open();
            Capture = new(camera, Source.Session, 1, 8, 6, startupReviewed: true);
            _ = Capture.Update(Context(0), 0);
        }
        private CaptureContext Context(double now) => new(now, camera, Source.Session, 1,
            AcquisitionMode.SoftwareSingle, true, true, true, true);
        public CaptureResult Offer()
        {
            time += .1; _ = Capture.Update(Context(time), time);
            CaptureRequest request = Capture.Prepare($"part-{trigger + 1}", time + .001)!;
            _ = Capture.TakeTrigger(time + .002);
            _ = Capture.Associate(new(request.Key, camera, ++trigger, true), time + .003);
            using FrameLease lease = Source.Acquire();
            CaptureFrame packet = new(lease.CopyToOwned(), camera, 1, trigger, time + .004, true, true);
            if (Capture.ObserveFrame(packet, time + .004) != CaptureFrameDecision.Matched) throw new InvalidOperationException("Fixture did not match.");
            return Capture.TakeForProcessing(time + .005)!;
        }
        public bool Release(CaptureResult result) => Capture.ReleaseCapture(result.Request.Key, time + .006);
        public CaptureResult Captured()
        {
            // Unit-test caller keeps the owned frame even though it releases its capture slot.
            CaptureResult result = Offer();
            if (!Release(result)) throw new InvalidOperationException("Fixture did not release.");
            return result;
        }
        public void Dispose() => Source.Dispose();
    }
    public static async Task Run()
    {
        count = 0;
        using Fixture f = new();
        Reject<ArgumentException>(() => _ = new InspectionPipeline(Guid.Empty, 8, 6, Recipe), "Empty capture owner rejected");
        Reject<ArgumentException>(() => _ = new InspectionPipeline(f.Capture.OwnerSession, 8, 6, Recipe, 0), "Zero capacity rejected");
        Reject<ArgumentException>(() => _ = new InspectionPipeline(f.Capture.OwnerSession, 8, 6, Recipe, 17), "Teaching capacity cap enforced");
        Reject<ArgumentException>(() => _ = new InspectionPipeline(f.Capture.OwnerSession, 8, 6, Recipe with { Version = "" }), "Empty algorithm version rejected");
        Reject<ArgumentException>(() => _ = new InspectionPipeline(f.Capture.OwnerSession, 8, 6, Recipe with { ProfileRevision = 0 }), "Unknown profile rejected");
        Reject<ArgumentException>(() => _ = new InspectionPipeline(f.Capture.OwnerSession, 8, 6, Recipe with { Region = new(7, 5, 2, 2) }), "Invalid ROI rejected before worker starts");

        CaptureResult a = f.Captured(), b = f.Captured(), c = f.Captured();
        TaskCompletionSource entered = Signal(), release = Signal(); int calls = 0, active = 0, maxActive = 0;
        InspectionPipeline pipeline = new(f.Capture.OwnerSession, 8, 6, Recipe, 2, async (image, plan) =>
        {
            int running = Interlocked.Increment(ref active); maxActive = Math.Max(maxActive, running);
            try
            {
                if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await release.Task; }
                return image.Measure(plan.Region, plan.Threshold);
            }
            finally { Interlocked.Decrement(ref active); }
        });
        try
        {
            Check(pipeline.Capacity == 2 && pipeline.RetainedCount == 0 && pipeline.Session != Guid.Empty, "Pipeline starts empty with bounded ledger and local session");
            InspectionSubmission first = pipeline.TrySubmit(a);
            Check(first.Admission == InspectionAdmission.Accepted && first.Result is not null, "Owned matched capture admitted with result task");
            await entered.Task.WaitAsync(TestLimit);
            Check(pipeline.Read(a.Request.Key)?.Stage == ProcessingStage.Processing && pipeline.RetainedCount == 1, "Running entry still occupies capacity");
            InspectionSubmission duplicate = pipeline.TrySubmit(a);
            Check(duplicate.Admission == InspectionAdmission.AlreadyTracked && ReferenceEquals(duplicate.Result, first.Result), "Same in-process offer returns original task, no duplicate run");
            Check(pipeline.TrySubmit(a with { Request = a.Request with { Workpiece = "changed" } }).Admission == InspectionAdmission.Conflict, "Changed offer under same key cannot replace original");
            Check(pipeline.TrySubmit(a with { }).Admission == InspectionAdmission.Conflict, "New record instance is conservatively conflicting, not content-deduplicated");
            InspectionSubmission second = pipeline.TrySubmit(b);
            Check(second.Admission == InspectionAdmission.Accepted && pipeline.RetainedCount == 2, "Queued plus running entries share total capacity");
            Check(pipeline.Read(b.Request.Key)?.Stage == ProcessingStage.Queued, "Second capture waits behind one worker");
            InspectionSubmission refused = pipeline.TrySubmit(c);
            Check(refused.Admission == InspectionAdmission.Full && refused.Result is null, "Full is explicit non-admission, no hidden task or drop");
            Check(pipeline.Read(c.Request.Key) is null && pipeline.RetainedCount == 2, "Refused offer does not create history or consume a slot");
            InspectionResult forged = new(pipeline.Session, a.Request, a.Evidence.Frame.Stamp, a.Evidence.CameraEpoch,
                1, Recipe, ProcessingStage.Completed, null, null);
            Check(!pipeline.Acknowledge(forged) && pipeline.RetainedCount == 2, "Cannot acknowledge a running entry or manufacture completion");
            release.SetResult();
            InspectionResult ra = await first.Result!.WaitAsync(TestLimit), rb = await second.Result!.WaitAsync(TestLimit);
            Check(ra.Stage == ProcessingStage.Completed && ra.ErrorCode is null, "Successful computation has a processing terminal, not product OK");
            Check(ra.Measurement is { Mean: 220, Count: 4, AtOrAboveThreshold: 4 }, "Real owned ROI pixels measured");
            Check(ra.Request == a.Request && ra.Frame == a.Evidence.Frame.Stamp && ra.CameraEpoch == a.Evidence.CameraEpoch
                && ra.TriggerSequence == a.Evidence.TriggerSequence && ra.Recipe == Recipe, "Result freezes request, source frame, trigger and algorithm identity");
            Check(rb.Request.Key == b.Request.Key && !ReferenceEquals(ra, rb), "Following result remains attached to its own request");
            Check(calls == 2 && maxActive == 1, "Exactly one algorithm invocation at a time; repeated offer did not run");
            Check(pipeline.RetainedCount == 2 && pipeline.TrySubmit(c).Admission == InspectionAdmission.Full, "Completed but unacknowledged results still exert backpressure");
            Check(!pipeline.Acknowledge(ra with { PipelineSession = Guid.NewGuid() }), "Foreign pipeline result cannot release capacity");
            Check(!pipeline.Acknowledge(ra with { }), "Copied receipt cannot impersonate original in-process result");
            Check(!pipeline.Acknowledge(ra with { Request = b.Request }), "Wrong request receipt cannot clear another result");
            Check(pipeline.Acknowledge(ra) && pipeline.RetainedCount == 1, "Explicit original-result acknowledgement frees one slot");
            Check(!pipeline.Acknowledge(ra), "Repeated acknowledgement has no effect");
            Check(pipeline.TrySubmit(a).Admission == InspectionAdmission.Stale, "Acknowledged old key cannot execute again in this pipeline");
            InspectionSubmission third = pipeline.TrySubmit(c);
            Check(third.Admission == InspectionAdmission.Accepted, "Full did not advance high-water; caller may explicitly reoffer held original");
            pipeline.Complete(); pipeline.Complete();
            Check(pipeline.TrySubmit(c).Admission == InspectionAdmission.Closed, "Complete closes admissions idempotently, even duplicate offers");
            InspectionResult rc = await third.Result!.WaitAsync(TestLimit);
            await pipeline.Completion.WaitAsync(TestLimit);
            Check(rc.Stage == ProcessingStage.Completed && calls == 3, "Close drains every admitted capture without new writes");
            Check(pipeline.RetainedCount == 2 && pipeline.Read(b.Request.Key)?.Result == rb, "Draining does not silently acknowledge retained results");
            Check(pipeline.Acknowledge(rb) && pipeline.Acknowledge(rc) && pipeline.RetainedCount == 0, "Results can be handed off after worker exits without outbox deadlock");
        }
        finally { release.TrySetResult(); await Finish(pipeline); }

        InspectionPipeline invalid = new(f.Capture.OwnerSession, 8, 6, Recipe);
        try
        {
            Check(invalid.TrySubmit(null!).Admission == InspectionAdmission.Invalid, "Null capture rejected");
            Check(invalid.TrySubmit(a with { Request = null! }).Admission == InspectionAdmission.Invalid, "Missing request rejected");
            Check(invalid.TrySubmit(a with { Evidence = null! }).Admission == InspectionAdmission.Invalid, "Missing evidence rejected");
            Check(invalid.TrySubmit(a with { Request = a.Request with { Key = new(Guid.NewGuid(), 1) } }).Admission == InspectionAdmission.Invalid, "Foreign capture owner rejected");
            Check(invalid.TrySubmit(a with { Request = a.Request with { Key = new(f.Capture.OwnerSession, 0) } }).Admission == InspectionAdmission.Invalid, "Zero request sequence rejected");
            Check(invalid.TrySubmit(a with { Request = a.Request with { Workpiece = "" } }).Admission == InspectionAdmission.Invalid, "Missing workpiece rejected");
            Check(invalid.TrySubmit(a with { Request = a.Request with { ProfileRevision = 2 } }).Admission == InspectionAdmission.Invalid, "Request profile mismatch rejected");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { ProfileRevision = 2 } }).Admission == InspectionAdmission.Invalid, "Image profile mismatch rejected");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { Complete = false } }).Admission == InspectionAdmission.Invalid, "Incomplete image rejected");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { IdentityQualified = false } }).Admission == InspectionAdmission.Invalid, "Unqualified image rejected");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { TriggerSequence = null } }).Admission == InspectionAdmission.Invalid, "Missing original trigger rejected");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { ReceivedAt = double.NaN } }).Admission == InspectionAdmission.Invalid, "Nonfinite timestamp rejected, no freshness claim");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { CameraEpoch = Guid.Empty } }).Admission == InspectionAdmission.Invalid, "Missing camera epoch rejected");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { Frame = new(new(Guid.Empty, 1), a.Evidence.Frame.Image) } }).Admission == InspectionAdmission.Invalid, "Missing source session rejected");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { Frame = new(new(Guid.NewGuid(), 0), a.Evidence.Frame.Image) } }).Admission == InspectionAdmission.Invalid, "Missing source frame number rejected");
            Check(invalid.TrySubmit(a with { Evidence = a.Evidence with { Frame = new(a.Evidence.Frame.Stamp, Mono8Image.FromPacked(1, 1, [0])) } }).Admission == InspectionAdmission.Invalid, "Unexpected image dimensions rejected");
            Check(invalid.RetainedCount == 0, "Invalid offers never allocate retained jobs");
            InspectionSubmission valid = invalid.TrySubmit(a);
            Check(valid.Admission == InspectionAdmission.Accepted && (await valid.Result!.WaitAsync(TestLimit)).Stage == ProcessingStage.Completed, "Invalid offers did not consume identity high-water");
        }
        finally { await Finish(invalid); }

        int failures = 0;
        InspectionPipeline failing = new(f.Capture.OwnerSession, 8, 6, Recipe, 3, (image, plan) =>
        {
            int call = Interlocked.Increment(ref failures);
            if (call == 1) throw new InvalidOperationException("Synthetic algorithm failure, not camera failure.");
            if (call == 2) throw new OperationCanceledException("No cancellation contract exists in this example.");
            return ValueTask.FromResult(image.Measure(plan.Region, plan.Threshold));
        });
        try
        {
            InspectionSubmission sa = failing.TrySubmit(a), sb = failing.TrySubmit(b), sc = failing.TrySubmit(c);
            InspectionResult ra = await sa.Result!.WaitAsync(TestLimit), rb = await sb.Result!.WaitAsync(TestLimit), rc = await sc.Result!.WaitAsync(TestLimit);
            Check(ra is { Stage: ProcessingStage.Failed, Measurement: null, ErrorCode: "InvalidOperationException" }, "Algorithm exception becomes keyed failure without stale measurement");
            Check(rb is { Stage: ProcessingStage.Failed, ErrorCode: "OperationCanceledException" }, "Uncontracted cancellation exception is failure, not invented safe cancellation");
            Check(rc.Stage == ProcessingStage.Completed && failures == 3, "Independent later job still runs after known algorithm failure");
            Check(failing.RetainedCount == 3 && failing.Read(a.Request.Key)?.Result == ra, "Failures remain available until explicit handoff");
            Check(failing.Acknowledge(ra) && failing.Acknowledge(rb) && failing.Acknowledge(rc), "Failure results require the same acknowledgement discipline");
        }
        finally { await Finish(failing); }

        InspectionPipeline malformed = new(f.Capture.OwnerSession, 8, 6, Recipe, measure: (_, _) => ValueTask.FromResult<PixelStatistics>(null!));
        try { Check((await malformed.TrySubmit(a).Result!.WaitAsync(TestLimit)).Stage == ProcessingStage.Failed, "Null algorithm summary cannot become completed measurement"); }
        finally { await Finish(malformed); }
        InspectionPipeline empty = new(f.Capture.OwnerSession, 8, 6, Recipe);
        await Finish(empty);
        Check(empty.Completion.IsCompletedSuccessfully && empty.RetainedCount == 0, "Empty pipeline closes without pending result reader");
        InspectionPipeline high = new(f.Capture.OwnerSession, 8, 6, Recipe);
        try
        {
            _ = await high.TrySubmit(c).Result!.WaitAsync(TestLimit);
            Check(high.TrySubmit(b).Admission == InspectionAdmission.Stale, "Out-of-order lower sequence rejected; serial capture contract is explicit");
        }
        finally { await Finish(high); }

        InspectionPipeline concurrent = new(f.Capture.OwnerSession, 8, 6, Recipe, 1);
        try
        {
            InspectionSubmission[] offers = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => concurrent.TrySubmit(a))));
            Check(offers.Count(x => x.Admission == InspectionAdmission.Accepted) == 1
                && offers.Count(x => x.Admission == InspectionAdmission.AlreadyTracked) == 31, "Concurrent duplicate offers admit only one entry");
            InspectionResult result = await offers.Single(x => x.Admission == InspectionAdmission.Accepted).Result!.WaitAsync(TestLimit);
            Check(!concurrent.Acknowledge(result with { Request = null! }) && concurrent.RetainedCount == 1, "Malformed acknowledgement cannot clear retained result");
            Check(concurrent.Acknowledge(result) && concurrent.RetainedCount == 0, "Concurrent offers still require one original receipt handoff");
        }
        finally { await Finish(concurrent); }

        LatestPreviewSlot preview = new();
        Check(preview.Take() is null, "Empty preview slot has no frame");
        Check(!preview.Publish(a.Evidence.Frame), "First preview publication replaces nothing");
        Check(preview.Publish(b.Evidence.Frame) && preview.Publish(c.Evidence.Frame), "Slow preview deliberately replaces unconsumed older publications");
        Check(ReferenceEquals(preview.Take(), c.Evidence.Frame) && preview.Take() is null, "Preview takes latest publication once, not an inspection queue");
        Check(a.Evidence.Frame.Image.GetPixel(1, 1) == 220, "Preview replacement cannot mutate shared immutable inspection pixels");
        Reject<ArgumentNullException>(() => preview.Publish(null!), "Null preview rejected");
        Reject<ArgumentException>(() => preview.Publish(new(new(Guid.Empty, 1), a.Evidence.Frame.Image)), "Unstamped preview rejected");
        f.Source.Close();
        Check(a.Evidence.Frame.Image.Measure(Recipe.Region, Recipe.Threshold).Mean == 220, "Owned processing pixels survive offline source closure");
        Console.WriteLine($"Pipeline checks passed: {count}. Offline only; no camera, product verdict or PLC write.");
    }
    public static async Task Demo()
    {
        using Fixture f = new();
        LatestPreviewSlot preview = new();
        InspectionPipeline pipeline = new(f.Capture.OwnerSession, 8, 6, Recipe, 1);
        try
        {
            CaptureResult owned = f.Offer();
            _ = preview.Publish(owned.Evidence.Frame);
            InspectionSubmission submitted = pipeline.TrySubmit(owned);
            if (submitted.Admission != InspectionAdmission.Accepted) throw new InvalidOperationException("Caller must retain image and resolve non-admission.");
            if (!f.Release(owned)) throw new InvalidOperationException("Capture release failed; reconcile instead of retriggering.");
            InspectionResult result = await submitted.Result!;
            Console.WriteLine($"{result.Request.Workpiece}: {result.Stage}, frame={result.Frame.Number}, recipe={result.Recipe.Version}, mean={result.Measurement?.Mean}");
            CaptureResult held = f.Offer();
            _ = preview.Publish(held.Evidence.Frame);
            Console.WriteLine($"Before result handoff: {pipeline.TrySubmit(held).Admission}; caller still holds capture/image.");
            _ = pipeline.Acknowledge(result); // Demo prints only; NOT durable storage or PLC acknowledgement.
            InspectionSubmission retry = pipeline.TrySubmit(held); // Explicit reoffer of same image, not another exposure.
            if (retry.Admission != InspectionAdmission.Accepted || !f.Release(held)) throw new InvalidOperationException("Handoff failed.");
            InspectionResult second = await retry.Result!;
            _ = pipeline.Acknowledge(second);
            Console.WriteLine($"After local handoff: {second.Request.Workpiece} {second.Stage}; retained={pipeline.RetainedCount}; preview frame={preview.Take()?.Stamp.Number}");
        }
        finally { pipeline.Complete(); await pipeline.Completion; }
    }
}
