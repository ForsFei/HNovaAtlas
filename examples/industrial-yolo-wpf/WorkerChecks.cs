using System.IO;
using IndustrialYolo;
using IndustrialYolo.Images;

namespace IndustrialYolo.Wpf;

internal static class WorkerChecks
{
    private static int count;
    private sealed class FakeEngine(Func<ImageSnapshot, OverlayResult>? process = null, bool failDispose = false) : IYoloEngine
    {
        public int Runs, Disposals, Active, Maximum;
        public OverlayResult Process(ImageSnapshot image)
        {
            Runs++; Active++; Maximum = Math.Max(Maximum, Active);
            try { return process?.Invoke(image) ?? new(image, [], 0); }
            finally { Active--; }
        }
        public void Dispose() { Disposals++; if (failDispose) throw new InvalidOperationException("Injected dispose failure."); }
    }
    private static ImageSnapshot Image()
    {
        Bgr24Image bgr = Bgr24Image.Create(20, 10, 60, new byte[600]);
        return ImageSnapshot.Create(new("handwritten-worker", "test-only", 0, 3, bgr));
    }
    private static YoloWork Job(YoloInferenceWorker worker, long n, ImageSnapshot image) => new(worker.Producer, n, image);
    private static async Task<YoloTerminal> Done(YoloSubmission submission) => await submission.Completion!.WaitAsync(TimeSpan.FromSeconds(10));
    public static async Task Managed()
    {
        count = 0; ImageSnapshot image = Image();
        Reject(() => new YoloInferenceWorker(() => new FakeEngine(), 0), "zero capacity");
        Reject(() => new YoloInferenceWorker("model", 1, float.NaN), "invalid score");
        using ManualResetEventSlim start = new(), release = new();
        TaskCompletionSource constructed = new(TaskCreationOptions.RunContinuationsAsynchronously), entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeEngine engine = new(frame => { entered.TrySetResult(); Wait(release); return new(frame, [], 0); });
        int factories = 0;
        YoloInferenceWorker worker = new(() => { Interlocked.Increment(ref factories); constructed.TrySetResult(); Wait(start); return engine; }, 2);
        await constructed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        YoloWork first = Job(worker, 1, image), second = Job(worker, 2, image), third = Job(worker, 3, image);
        Check(worker.TrySubmit(first).Admission == YoloAdmission.Starting, "initializing rejects without transfer");
        Check(worker.Snapshot.Accepted == 0 && !worker.Ready.IsCompleted, "starting has no accepted jobs");
        start.Set(); await worker.Ready;
        Check(worker.Snapshot.Ready && factories == 1, "factory opened once");
        Check(worker.TrySubmit(null!).Admission == YoloAdmission.Invalid, "null job");
        Check(worker.TrySubmit(first with { Producer = Guid.NewGuid() }).Admission == YoloAdmission.Invalid, "foreign producer");
        Check(worker.TrySubmit(first with { Number = 0 }).Admission == YoloAdmission.Invalid, "zero number");
        YoloSubmission one = worker.TrySubmit(first); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(one.Admission == YoloAdmission.Accepted && worker.Snapshot.Running == 1, "first runs");
        YoloSubmission repeat = worker.TrySubmit(first);
        Check(repeat.Admission == YoloAdmission.AlreadyTracked && ReferenceEquals(repeat.Completion, one.Completion), "same job same promise");
        Check(worker.TrySubmit(first with { }).Admission == YoloAdmission.Conflict, "copied job conflicts");
        YoloSubmission two = worker.TrySubmit(second);
        Check(two.Admission == YoloAdmission.Accepted && worker.Snapshot.Queued == 1, "second queued");
        Check(worker.TrySubmit(third) is { Admission: YoloAdmission.Full, Completion: null }, "running plus queued fills ledger");
        Check(worker.Snapshot.Retained == 2 && worker.Snapshot.Accepted == 2, "full does not transfer");
        release.Set(); YoloTerminal t1 = await Done(one), t2 = await Done(two);
        Check(engine.Runs == 2 && engine.Maximum == 1, "serial processing on reused engine");
        Check(worker.Snapshot.Unacknowledged == 2 && worker.Snapshot.Running == 0, "terminals still consume slots");
        Check(worker.TrySubmit(third).Admission == YoloAdmission.Full, "slow consumer backpressure");
        Check(ReferenceEquals(worker.ReadTerminal(1), t1), "terminal retained for explicit read");
        Check(!worker.Acknowledge(t1 with { }) && !worker.Acknowledge(new(worker.Session, null!, YoloStage.Completed, null, null)), "copied/malformed receipt refused");
        Check(worker.Acknowledge(t1) && !worker.Acknowledge(t1), "receipt once");
        Check(worker.TrySubmit(first).Admission == YoloAdmission.Stale, "acknowledged number never reruns");
        YoloSubmission three = worker.TrySubmit(third); YoloTerminal t3 = await Done(three);
        Check(three.Admission == YoloAdmission.Accepted, "full rejection did not advance number high water");
        Check(worker.Acknowledge(t2) && worker.Acknowledge(t3), "consume remaining receipts");
        worker.Complete(); worker.Complete(); await worker.Completion;
        Check(engine.Disposals == 1 && factories == 1, "single disposal by owner");
        Check(worker.Snapshot is { Retained: 0, Accepted: 3, Completed: 3, Failed: 0, Acknowledged: 3, Closed: true, Faulted: false }, "normal accounting");
        Check(worker.TrySubmit(Job(worker, 4, image)).Admission == YoloAdmission.Closed, "closed admission");

        FakeEngine parallelEngine = new(); YoloInferenceWorker concurrent = new(() => parallelEngine, 1); await concurrent.Ready;
        YoloWork same = Job(concurrent, 1, image);
        YoloSubmission[] responses = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => concurrent.TrySubmit(same))));
        Check(responses.Count(s => s.Admission == YoloAdmission.Accepted) == 1 && responses.Count(s => s.Admission == YoloAdmission.AlreadyTracked) == 31, "32 duplicate producers one transfer");
        Check(responses.Select(s => s.Completion).Distinct().Count() == 1, "concurrent duplicate same completion");
        YoloTerminal ct = await Done(responses[0]); concurrent.Complete(); await concurrent.Completion;
        Check(parallelEngine.Runs == 1 && concurrent.Snapshot.Unacknowledged == 1, "close does not auto acknowledge");
        Check(concurrent.Acknowledge(ct) && concurrent.Snapshot.Retained == 0, "can consume terminal after worker shutdown");

        await DrainAndDisplay(image);
        await Failures(image);
        Console.WriteLine($"PASS {count} gated ledger/reuse/UI-handoff checks. Injected engines only; no model/codec/window.");
    }
    private static async Task DrainAndDisplay(ImageSnapshot image)
    {
        using ManualResetEventSlim release = new(); TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeEngine engine = new(frame => { entered.TrySetResult(); Wait(release); return new(frame, [], 0); });
        YoloInferenceWorker worker = new(() => engine, 2); await worker.Ready;
        DisplaySession display = new(); display.Show(image); InferenceRequest selection = display.TryBegin()!;
        YoloSubmission first = worker.TrySubmit(Job(worker, 1, image)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        YoloSubmission second = worker.TrySubmit(Job(worker, 2, image));
        display.Show(Image()); worker.Complete();
        Check(!worker.Completion.IsCompleted && engine.Disposals == 0, "close never races running disposal");
        Check(worker.TrySubmit(Job(worker, 3, image)).Admission == YoloAdmission.Closed, "draining refuses new work");
        release.Set(); YoloTerminal t1 = await Done(first), t2 = await Done(second); await worker.Completion;
        Check(display.Complete(selection, t1.Output) == Attachment.Stale && display.Result is null, "old result not attached to new image");
        Check(worker.Acknowledge(t1), "stale-for-display still explicitly consumed");
        Check(t2.Stage == YoloStage.Completed && engine.Runs == 2 && engine.Disposals == 1, "accepted queued job drains before disposal");
        Check(worker.Snapshot is { Unacknowledged: 1, Queued: 0, Running: 0, Completed: 2 }, "shutdown retains unread terminal");
        Check(worker.Acknowledge(t2), "drained result acknowledged explicitly");
    }
    private static async Task Failures(ImageSnapshot image)
    {
        using ManualResetEventSlim release = new(); TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeEngine broken = new(_ => { entered.TrySetResult(); Wait(release); throw new InvalidDataException("Injected failure"); });
        YoloInferenceWorker worker = new(() => broken, 2); await worker.Ready;
        YoloSubmission first = worker.TrySubmit(Job(worker, 1, image)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        YoloSubmission waiting = worker.TrySubmit(Job(worker, 2, image)); release.Set();
        YoloTerminal failed = await Done(first), skipped = await Done(waiting);
        Check(worker.TrySubmit(Job(worker, 3, image)).Admission == YoloAdmission.Closed, "failure publication cannot reopen admission");
        Check(failed is { Stage: YoloStage.Failed, Output: null, Error: "ProcessingFailed:InvalidDataException" }, "running failure not empty success");
        Check(skipped is { Stage: YoloStage.Failed, Output: null, Error: "WorkerStoppedBeforeRun" }, "queued accepted job gets explicit failure without retry");
        Check(await Faults(worker.Completion), "worker completion reports fatal failure");
        Check(worker.Ready.IsCompletedSuccessfully && worker.Snapshot.Faulted && worker.Snapshot.Closed, "ready fact does not hide later failure");
        Check(broken.Runs == 1 && broken.Disposals == 1 && worker.Snapshot.Unacknowledged == 2, "fail stop no second native call or lost receipt");
        Check(worker.Acknowledge(failed) && worker.Acknowledge(skipped), "failed receipts consume like successes");
        YoloInferenceWorker startup = new(() => throw new IOException("Injected model load failure"), 1);
        Check(await Faults(startup.Ready) && await Faults(startup.Completion), "startup ready and completion fail");
        Check(startup.TrySubmit(Job(startup, 1, image)).Admission == YoloAdmission.Closed && startup.Snapshot.Accepted == 0, "startup never accepts work");
        FakeEngine disposal = new(failDispose: true); YoloInferenceWorker disposeWorker = new(() => disposal, 1); await disposeWorker.Ready;
        disposeWorker.Complete(); Check(await Faults(disposeWorker.Completion) && disposeWorker.Snapshot.Faulted, "dispose error is observable");
        using ManualResetEventSlim initialize = new();
        TaskCompletionSource constructing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeEngine earlyEngine = new();
        YoloInferenceWorker earlyClose = new(() => { constructing.SetResult(); Wait(initialize); return earlyEngine; }, 1);
        await constructing.Task.WaitAsync(TimeSpan.FromSeconds(10)); earlyClose.Complete();
        Check(!earlyClose.Completion.IsCompleted && earlyEngine.Disposals == 0 &&
            earlyClose.TrySubmit(Job(earlyClose, 1, image)).Admission == YoloAdmission.Closed, "close during initialization waits without admission");
        initialize.Set(); await earlyClose.Ready; await earlyClose.Completion;
        Check(earlyEngine.Disposals == 1 && earlyClose.Snapshot.Accepted == 0 && !earlyClose.Snapshot.Faulted, "initialized-after-close engine disposed once");
    }
    public static async Task Native(string model)
    {
        count = 0; ReusedYoloEngine? actual = null; int created = 0;
        ImageSnapshot image = await Task.Run(() =>
        {
            byte[] packed = new byte[641 * 359 * 3];
            for (int y = 0; y < 359; y++) for (int x = 0; x < 641; x++)
            { int i = (y * 641 + x) * 3; packed[i] = (byte)(x % 256); packed[i + 1] = (byte)(y % 256); packed[i + 2] = (byte)((x + 2 * y) % 256); }
            return ImageSnapshot.Create(new("owned-gradient", "not-encoded", 0, 3, Bgr24Image.Create(641, 359, 1923, packed)));
        });
        YoloInferenceWorker worker = new(() => { Interlocked.Increment(ref created); return actual = new(model, .01f); }, 2);
        await worker.Ready; Check(created == 1 && actual is not null, "one real session opened");
        YoloWork job = Job(worker, 1, image); YoloSubmission one = worker.TrySubmit(job);
        Check(worker.TrySubmit(job).Admission == YoloAdmission.AlreadyTracked, "native duplicate not rerun");
        YoloSubmission two = worker.TrySubmit(Job(worker, 2, image));
        YoloTerminal first = await Done(one), second = await Done(two);
        Check(first.Stage == YoloStage.Completed && second.Stage == YoloStage.Completed, "two real runs completed");
        Check(first.Output!.Boxes.Select(b => b.Row).SequenceEqual(new[] { 3439, 3468, 3462, 3454, 3420 }), "same offline reference rows");
        Check(first.Output.Boxes.SequenceEqual(second.Output!.Boxes) && !ReferenceEquals(first.Output, second.Output), "repeat numerical agreement with separate results");
        Check(worker.TrySubmit(Job(worker, 3, image)).Admission == YoloAdmission.Full, "native terminal backpressure");
        Check(worker.Acknowledge(first), "native receipt consumption");
        YoloTerminal third = await Done(worker.TrySubmit(Job(worker, 3, image)));
        worker.Complete(); await worker.Completion;
        Check(actual!.Runs == 3 && actual.Disposed && created == 1, "three Run calls one session disposed after drain");
        Check(worker.Snapshot is { Accepted: 3, Completed: 3, Failed: 0, Unacknowledged: 2, Running: 0, Queued: 0 }, "native ledger balances after shutdown");
        Check(worker.Acknowledge(second) && worker.Acknowledge(third) && worker.Snapshot.Retained == 0, "native explicit final consume");
        YoloInferenceWorker missing = new(System.IO.Path.Combine(AppContext.BaseDirectory, $"missing-{Guid.NewGuid():N}.onnx"));
        Check(await Faults(missing.Ready) && await Faults(missing.Completion), "real missing-model failure has no fallback");
        Check(missing.Snapshot is { Closed: true, Faulted: true, Accepted: 0, Retained: 0 }, "failed real initialization transfers nothing");
        Console.WriteLine($"PASS {count} actual reused CPU-session/OpenCV/WPF handoff checks. Three runs/one session; no FPS or long-memory acceptance.");
    }
    private static void Wait(ManualResetEventSlim gate)
    { if (!gate.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test gate deadline; not an inference cancellation guarantee."); }
    private static async Task<bool> Faults(Task task)
    { try { await task.WaitAsync(TimeSpan.FromSeconds(10)); return false; } catch (InvalidOperationException) { return true; } catch (IOException) { return true; } }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException($"Worker check failed: {name}"); count++; }
    private static void Reject(Action action, string name)
    { try { action(); } catch (ArgumentException) { count++; return; } throw new InvalidOperationException($"Expected worker rejection: {name}"); }
}
