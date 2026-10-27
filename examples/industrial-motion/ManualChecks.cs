namespace IndustrialMotion;

internal static class ManualChecks
{
    private static int _count;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, label); return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + label);
    }
    private static void Finish(ManualAxisSession session)
    {
        for (int i = 0; session.Snapshot.Busy && i < 1000; i++) session.Tick(.01);
        if (session.Snapshot.Busy) throw new InvalidOperationException("Simulation step budget exceeded.");
    }
    private static ManualAxisSession Ready()
    {
        var session = new ManualAxisSession(); session.SetContext(true, true, true);
        session.Open(); session.Enable(); session.Home(); Finish(session); session.ObserveAllReleased();
        return session;
    }
    public static void Demo()
    {
        using var session = Ready(); JogHandle handle = session.Press(1, .5); session.Tick(.1);
        session.Release(handle);
        Console.WriteLine($"Release: {session.Snapshot.State}, busy={session.Snapshot.Busy}");
        Finish(session); Console.WriteLine($"After simulated stop: {session.Snapshot.Result}, armed={session.Armed}");
        session.InjectInputs(new(Alarm: true)); session.InjectInputs(new()); session.ResetFault();
        Console.WriteLine($"Reset: {session.Snapshot.State}, enabled={session.Snapshot.Enabled}, homed={session.Snapshot.Homed}");
        session.Close(); Console.WriteLine("Pure simulation; no native SDK commands or hardware accessed.");
    }
    public static void Run()
    {
        using (var session = new ManualAxisSession())
        {
            session.Open(); Throws<InvalidOperationException>(session.Enable, "Enable requires explicit manual authority");
            session.SetContext(true, true, true); session.Enable(); session.ObserveAllReleased();
            Throws<InvalidOperationException>(() => session.Press(1, .5), "Manual jog requires model reference");
            session.Home(); Finish(session);
            Check(!session.Armed, "Home completion does not fabricate a fresh release");
            session.ObserveAllReleased(); Check(session.Armed, "Fresh release plus ready/reference allows a new press");
            JogHandle first = session.Press(1, .5); Check(session.Held == first && !session.Armed, "One hold owns one simulated jog");
            Throws<InvalidOperationException>(() => session.Press(-1, .5), "No overlapping direction or queued reverse");
            Throws<InvalidOperationException>(() => session.Home(), "Home cannot compete with held jog");
            session.Tick(.1); Check(session.Renew(first), "Only current hold renews the simulated lease");
            Check(session.Release(first) && session.Snapshot.State == AxisState.Stopping && session.Held == null, "Release revokes hold before stop confirmation");
            Check(!session.Renew(first) && !session.Release(first), "Released hold cannot renew or stop twice");
            Throws<InvalidOperationException>(session.Close, "Resource close refuses active deceleration");
            Throws<InvalidOperationException>(session.Disable, "Disable is not the ordinary stop command");
            Finish(session); Check(session.Snapshot.Result == CommandResult.Stopped && !session.Armed, "Stopped does not complete a target or rearm held input");
            session.ObserveAllReleased(); JogHandle second = session.Press(-1, .5);
            Check(second.Sequence > first.Sequence && second.Command != first.Command, "New press gets new local and motion identities");
            Check(!session.Release(first) && session.Held == second, "Delayed old release cannot stop newer motion");
            Check(!session.Renew(first) && session.Held == second, "Delayed old renewal cannot extend newer motion");
            session.Stop("OperatorStop"); Finish(session); session.Disable(); session.Close();
            Check(!session.Snapshot.DataValid && !session.Snapshot.Homed, "Normal close invalidates model data/reference");
        }
        foreach (var context in new[] { (false, true, true), (true, false, true), (true, true, false) })
        {
            using var session = Ready(); var handle = session.Press(1, .5); session.Tick(.01);
            session.SetContext(context.Item1, context.Item2, context.Item3);
            Check(session.Held == null && session.Snapshot.State == AxisState.Stopping, "Focus/mode/interlock loss requests stop and drops hold");
            session.ObserveAllReleased(); Finish(session); session.SetContext(true, true, true);
            Check(!session.Armed && !session.Renew(handle), "Restoring authority does not revive old held input");
            session.ObserveAllReleased(); Check(session.Armed, "New all-released observation needed after restoration");
        }
        using (var session = Ready())
        {
            var handle = session.Press(1, .5);
            for (int i = 0; i < 3; i++) session.Tick(.1);
            Check(session.Held == null && !session.Renew(handle), "Lease expires only as simulator time advances and cannot be revived");
            Finish(session); Check(session.Snapshot.Result == CommandResult.Stopped, "Expired lease eventually reaches simulated standstill");
        }
        using (var session = Ready())
        {
            var handle = session.Press(1, .5); session.InjectInputs(new(Alarm: true));
            Check(session.Held == null && session.Snapshot.State == AxisState.Faulted && !session.Snapshot.Homed, "Fault drops manual hold and model reference");
            Throws<InvalidOperationException>(session.ResetFault, "Reset refused while alarm remains");
            session.InjectInputs(new()); Check(session.Snapshot.State == AxisState.Faulted, "Removing alarm input does not silently reset");
            session.ResetFault();
            Check(session.Snapshot.State == AxisState.Disabled && !session.Snapshot.Enabled && !session.Snapshot.Homed && session.Snapshot.Result == CommandResult.Faulted,
                "Explicit reset neither enables, homes, replays nor turns old failure into completion");
            Check(!session.Renew(handle), "Fault-reset sequence never revives old hold");
            session.Enable(); session.ObserveAllReleased(); Check(!session.Armed, "Explicit enable alone cannot restore reference");
        }
        using (var session = Ready())
        {
            session.Press(1, .5); session.InjectInputs(new(BusOperational: false));
            Check(!session.Snapshot.DataValid && session.Snapshot.Result == CommandResult.Uncertain && session.Held == null, "Bus loss preserves unknown outcome, not stopped");
            Throws<InvalidOperationException>(session.ResetFault, "No reset while bus diagnostic is unhealthy");
        }
        using (var old = Ready())
        using (var replacement = Ready())
        {
            var oldHandle = old.Press(1, .5); var freshHandle = replacement.Press(1, .5);
            Check(oldHandle.Command == freshHandle.Command && oldHandle.Session != freshHandle.Session, "Rebuilt model cannot collide even if command numbers restart");
            Check(!replacement.Release(oldHandle) && replacement.Held == freshHandle, "Old-window hold cannot release a replacement session");
            old.Stop("TestCleanup"); replacement.Stop("TestCleanup"); Finish(old); Finish(replacement);
        }
        using (var session = Ready())
        {
            Throws<ArgumentOutOfRangeException>(() => session.Press(1, 3), "Reject manual speed beyond simulator cap");
            Check(session.Held == null && !session.Armed && !session.Snapshot.Busy, "Invalid press does not queue motion or retain rearm");
            session.ObserveAllReleased(); Throws<ArgumentOutOfRangeException>(() => session.Press(0, .5), "Invalid direction rejected");
        }
        var disposed = Ready(); var held = disposed.Press(1, .5); disposed.Dispose();
        Check(!disposed.Snapshot.DataValid && disposed.Snapshot.Result == CommandResult.Uncertain, "Forced fake disposal during jog is not stop evidence");
        Throws<ObjectDisposedException>(() => disposed.Renew(held), "Disposed session refuses old renewals");
        Console.WriteLine($"Completed {_count} manual checks. Pure simulator only; no native SDK or hardware accessed.");
    }
}
