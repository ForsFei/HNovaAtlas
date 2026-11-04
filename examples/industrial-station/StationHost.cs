using System.Threading.Channels;

namespace IndustrialStation;

public interface IStationAsyncPort : IAsyncDisposable
{
    Task<StationReply?> ExecuteAsync(StationCommand command);
    Task<StationReply?> ObserveAsync(StationCommand command); // Read-only reconciliation, never another Execute.
}
public sealed record PortObservation(OperationKey Key, bool ReadOnlyQuery, string Result, ReplyDisposition? Disposition);
public sealed record HostSnapshot(long Revision, StationSnapshot Station, bool TaskActive, bool QueryActive,
    bool NeedsReconciliation, bool CloseRequested, bool PortDisposed, PortObservation? LastObservation);

// One logical owner mutates the reducer. SDK-like work runs outside that owner and outside reducer locks.
public sealed class StationHost
{
    private abstract class Message { public abstract void Run(StationHost host); public abstract void Fail(Exception error); }
    private sealed class Call<T>(Func<T> action) : Message
    {
        public TaskCompletionSource<T> Promise { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Run(StationHost host)
        {
            try { T result = action(); host.Publish(); Promise.TrySetResult(result); }
            catch (Exception error) { Promise.TrySetException(error); }
        }
        public override void Fail(Exception error) => Promise.TrySetException(error);
    }
    private readonly StationController controller;
    private readonly IStationAsyncPort port;
    private readonly Channel<Message> mailbox;
    private readonly int batchLimit;
    private readonly object admission = new(), snapshots = new();
    private readonly TaskCompletionSource closeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<HostSnapshot> change = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HostSnapshot latest;
    private bool closeRequested, stopped, closeApplied, disposed, query;
    private Task<StationReply?>? active;
    private StationCommand? activeCommand;
    private PortObservation? last;
    public Guid Producer => controller.Producer;
    public Task Completion { get; }
    public HostSnapshot Snapshot { get { lock (snapshots) return latest; } }
    public StationHost(Guid producer, IStationAsyncPort port, TimeProvider? clock = null, bool automaticTicks = true, int mailboxCapacity = 16)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (mailboxCapacity is < 1 or > 64) throw new ArgumentException("Mailbox capacity must be1..64.");
        controller = new(producer, clock); this.port = port; batchLimit = mailboxCapacity;
        mailbox = Channel.CreateBounded<Message>(new BoundedChannelOptions(mailboxCapacity)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        latest = new(0, controller.Snapshot, false, false, false, false, false, null);
        Completion = Task.Run(() => LoopAsync(automaticTicks));
    }
    public Task<Admission> StartAsync(WorkOrder work, StartEvidence evidence) => IsClosing ? Task.FromResult(Admission.Closed) :
        Post(() => IsClosing ? Admission.Closed : controller.TryStart(work, evidence));
    public Task PulseAsync() => Post(() => { controller.Tick(); return true; });
    public Task StopAsync() => Post(() => { controller.Stop(); return true; });
    public Task<bool> ConsumeAsync(StationTerminal terminal) => Post(() => controller.ConsumeTerminal(terminal));
    public Task<bool> ResetAsync(RecoveryEvidence evidence) => Post(() => active is null && !IsClosing && controller.Reset(evidence));
    public Task<bool> ReleaseAsync(RecoveryEvidence evidence) => Post(() => active is null && !IsClosing && controller.ReleaseCompletedCycle(evidence));
    public Task<bool> ReconcileAsync() => Post(() =>
    {
        StationSnapshot s = controller.Snapshot;
        if (active is not null || s.Pending is null || !s.CommandTaken) return false;
        Launch(s.Pending, readOnly: true); return true;
    });
    public void RequestClose()
    {
        lock (admission) { if (closeRequested || stopped) return; closeRequested = true; closeSignal.TrySetResult(); }
    }
    public Task<HostSnapshot> ObserveAfterAsync(long revision)
    {
        lock (snapshots) return latest.Revision > revision || Completion.IsCompleted ? Task.FromResult(latest) : change.Task;
    }
    private bool IsClosing { get { lock (admission) return closeRequested; } }
    private Task<T> Post<T>(Func<T> action)
    {
        Call<T> message = new(action);
        lock (admission)
        {
            if (stopped) return Task.FromException<T>(new InvalidOperationException("HostClosed"));
            if (!mailbox.Writer.TryWrite(message)) return Task.FromException<T>(new InvalidOperationException("MailboxFull: request was not accepted."));
        }
        return message.Promise.Task;
    }
    private void Publish()
    {
        StationSnapshot s = controller.Snapshot;
        lock (snapshots)
        {
            latest = new(latest.Revision + 1, s, active is not null, query && active is not null,
                active is null && s.Pending is not null && s.CommandTaken, IsClosing, disposed, last);
            TaskCompletionSource<HostSnapshot> previous = change;
            change = new(TaskCreationOptions.RunContinuationsAsynchronously); previous.TrySetResult(latest);
        }
    }
    private void Launch(StationCommand command, bool readOnly)
    {
        activeCommand = command; query = readOnly;
        // Even a blocking prefix before the first await cannot block the state owner.
        active = Task.Run(() => readOnly ? port.ObserveAsync(command) : port.ExecuteAsync(command));
    }
    private void ObserveFinished()
    {
        StationCommand command = activeCommand!; Task<StationReply?> task = active!; bool wasQuery = query;
        active = null; activeCommand = null; query = false;
        controller.Tick();
        try
        {
            StationReply? reply = task.GetAwaiter().GetResult(); // Only called after IsCompleted.
            if (reply is null)
            {
                controller.RecordUncertain(command.Key, "NoTerminal"); last = new(command.Key, wasQuery, "NoTerminal", null);
            }
            else
            {
                ReplyDisposition outcome = controller.Receive(reply);
                if (outcome is ReplyDisposition.IgnoredCorrelation or ReplyDisposition.RejectedPayload or ReplyDisposition.NoPending)
                    controller.RecordUncertain(command.Key, "ReplyRejected");
                last = new(command.Key, wasQuery, "Returned", outcome);
            }
        }
        catch (Exception error)
        {
            string code = error is OperationCanceledException ? "TaskCanceled" : "TaskFault";
            controller.RecordUncertain(command.Key, code); last = new(command.Key, wasQuery, $"{code}:{error.GetType().Name}", null);
        }
        Publish();
    }
    private async Task LoopAsync(bool automaticTicks)
    {
        using CancellationTokenSource timerStop = new();
        Task? timer = automaticTicks ? Task.Delay(TimeSpan.FromMilliseconds(25), timerStop.Token) : null;
        Task<bool>? readable = null;
        Exception? fatal = null;
        try
        {
            while (true)
            {
                if (IsClosing && !closeApplied) { controller.RequestClose(); closeApplied = true; Publish(); }
                if (active?.IsCompleted == true) ObserveFinished();
                for (int i = 0; i < batchLimit && mailbox.Reader.TryRead(out Message? message); i++) message.Run(this);
                bool launched = false;
                lock (admission)
                {
                    if (!closeRequested && active is null && controller.TryTakeCommand() is { } command)
                    { Launch(command, false); launched = true; }
                }
                if (launched) Publish();
                if (controller.Snapshot.Stage == StationStage.Closed && active is null)
                {
                    lock (admission) { stopped = true; mailbox.Writer.TryComplete(); }
                    while (mailbox.Reader.TryRead(out Message? message)) message.Run(this);
                    break;
                }
                readable ??= mailbox.Reader.WaitToReadAsync().AsTask();
                List<Task> waits = [readable];
                if (active is not null) waits.Add(active);
                if (timer is not null) waits.Add(timer);
                if (!closeApplied) waits.Add(closeSignal.Task);
                await Task.WhenAny(waits).ConfigureAwait(false);
                if (readable.IsCompleted) { _ = await readable.ConfigureAwait(false); readable = null; }
                if (timer?.IsCompleted == true)
                {
                    await timer.ConfigureAwait(false); controller.Tick(); Publish();
                    timer = Task.Delay(TimeSpan.FromMilliseconds(25), timerStop.Token);
                }
            }
        }
        catch (Exception error) { fatal = error; }
        finally
        {
            lock (admission) { stopped = true; mailbox.Writer.TryComplete(); }
            while (mailbox.Reader.TryRead(out Message? message)) message.Fail(fatal ?? new InvalidOperationException("HostClosed"));
            timerStop.Cancel();
            // Unexpected owner failure still must not dispose underneath an actual call.
            if (active is not null)
            {
                try { await active.ConfigureAwait(false); } catch (Exception) { /* Observed, not treated as physical completion. */ }
                active = null;
            }
            if (controller.Snapshot.Pending is null)
            {
                try { await port.DisposeAsync().ConfigureAwait(false); disposed = true; }
                catch (Exception error) { fatal = fatal is null ? error : new AggregateException(fatal, error); }
            }
            else fatal ??= new InvalidOperationException("Unsettled command remains; port retained, not reported disposed.");
            Publish();
        }
        if (fatal is not null) throw new InvalidOperationException("Host failed; inspect obligations and cleanup status.", fatal);
    }
}
