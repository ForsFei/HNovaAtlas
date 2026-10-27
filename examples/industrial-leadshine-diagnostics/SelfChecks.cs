namespace IndustrialLeadshineDiagnostics;

internal static class SelfChecks
{
    private static int _count;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine($"PASS {++_count}: {message}");
    }
    private static T Throws<T>(Action call, string message) where T : Exception
    {
        try { call(); }
        catch (T error) { Check(true, message); return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + message);
    }
    public static void Run()
    {
        var config = FakeReadOnlyDmc.Configuration;
        Throws<ArgumentOutOfRangeException>(() => new CardProbe(new FakeReadOnlyDmc(), config with { Axis = 255 }), "Reject wildcard axis");
        Throws<ArgumentOutOfRangeException>(() => new CardProbe(new FakeReadOnlyDmc(), config with { CountsPerUnit = double.NaN }), "Reject nonfinite scale");
        Throws<ArgumentOutOfRangeException>(() => new CardProbe(new FakeReadOnlyDmc(), config with { MaxSampleDuration = TimeSpan.Zero }), "Reject zero sampling budget");
        var api = new FakeReadOnlyDmc(); var clock = new ManualClock();
        using (var probe = new CardProbe(api, config, clock))
        {
            Throws<InvalidOperationException>(() => probe.Sample(), "No sampling before open");
            probe.Open(); Check(probe.IsOpen && probe.Latest == null, "Initialization does not fabricate a sample");
            Throws<InvalidOperationException>(() => probe.Open(), "No duplicate resource initialization");
            var first = probe.Sample();
            Check(first.Card.Id == 3 && first.CommandPosition == 12.5 && first.DriveState == 1 && first.Node == config.ExpectedNode,
                "Card ID is not card count; disabled drive can be observed");
            Check(probe.IsCurrent(first, TimeSpan.FromSeconds(2)), "Fresh successful sample is current");
            clock.Advance(TimeSpan.FromSeconds(3));
            Check(!probe.IsCurrent(first, TimeSpan.FromSeconds(2)), "Age is measured from earliest read with monotonic time");
            var second = probe.Sample();
            Check(!probe.IsCurrent(first, TimeSpan.FromSeconds(20)) && probe.IsCurrent(second, TimeSpan.FromSeconds(2)), "Superseded snapshot is not current");
            api.Drive = 7; api.Alarm = 17;
            var fault = probe.Sample();
            Check(fault.DriveState == 7 && fault.AxisError == 17 && probe.IsCurrent(fault, TimeSpan.FromSeconds(2)), "Valid diagnostics can describe a fault; validity is not motion permission");
            api.FailOperation = "Position";
            Throws<DiagnosticException>(() => probe.Sample(), "SDK error does not publish a partial sample");
            Check(probe.Latest == null && !probe.IsCurrent(fault, TimeSpan.FromDays(1)) && probe.LastFailure!.Contains("23"), "Read failure invalidates previous values with raw error code");
            api.FailOperation = null; var recovered = probe.Sample();
            Check(probe.LastFailure == null && probe.IsCurrent(recovered, TimeSpan.FromSeconds(2)), "Explicit later diagnostic read can recover without writes");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            int before = api.Calls.Count;
            Throws<OperationCanceledException>(() => probe.Sample(cancelled.Token), "Pre-cancelled sample refused");
            Check(api.Calls.Count == before && probe.Latest == null, "Cancellation invalidates but makes no SDK call");
            var oldSession = probe.Sample(); probe.Close();
            Check(!probe.IsOpen && probe.Latest == null && !probe.IsCurrent(oldSession, TimeSpan.FromDays(1)), "Close invalidates observation, not proof of axis stop");
            probe.Open(); var newSession = probe.Sample();
            Check(newSession.Session != oldSession.Session && !probe.IsCurrent(oldSession, TimeSpan.FromDays(1)), "Reopen separates session identities");
            probe.Close(); probe.Close();
            Check(api.Calls.Count(x => x == "Close") == 2, "One close per initialization attempt; explicit close is idempotent");
        }
        foreach (short result in new short[] { 0, -4, 9 })
        {
            var bad = new FakeReadOnlyDmc { InitResult = result };
            using var probe = new CardProbe(bad, config);
            Throws<DiagnosticException>(() => probe.Open(), $"Reject initialization count/status {result}");
            Check(bad.Calls.SequenceEqual(new[] { "Init", "Close" }) && !probe.IsOpen, "Failed initialization gets cleanup, no card reads");
        }
        foreach (var cards in new[] { Array.Empty<CardIdentity>(), new[] { new CardIdentity(2, 0x1234) }, new[] { new CardIdentity(3, 99) } })
        {
            var bad = new FakeReadOnlyDmc { Identities = cards };
            using var probe = new CardProbe(bad, config);
            Throws<DiagnosticException>(() => probe.Open(), "Wrong card count, ID or firmware type refused");
            Check(bad.Calls.Last() == "Close", "Identity failure releases resources");
        }
        {
            var bad = new FakeReadOnlyDmc { InitResult = 2, Identities = [new(3, 0x1234), new(3, 0x1234)] };
            using var probe = new CardProbe(bad, config);
            Throws<DiagnosticException>(() => probe.Open(), "Duplicate hardware IDs refused");
        }
        (string Name, Action<FakeReadOnlyDmc> Change)[] mutations =
        [
            ("simulation mode", a => a.Mode = 0), ("bus diagnostic nonzero", a => a.Bus = 8),
            ("slave-count mismatch", a => a.Slaves = 1), ("axis outside range", a => a.Axes = 0),
            ("virtual instead of bus axis", a => a.Type = 0), ("node mapping mismatch", a => a.Node = new(1002, 0)),
            ("scale mismatch", a => a.Scale = 1), ("infinite scale", a => a.Scale = double.PositiveInfinity),
            ("unknown drive state", a => a.Drive = 42), ("NaN position", a => a.Position = double.NaN),
            ("infinite speed", a => a.Speed = double.NegativeInfinity)
        ];
        foreach (var mutation in mutations)
        {
            var bad = new FakeReadOnlyDmc(); using var probe = new CardProbe(bad, config);
            probe.Open(); var old = probe.Sample(); mutation.Change(bad);
            Throws<DiagnosticException>(() => probe.Sample(), "Reject " + mutation.Name);
            Check(probe.Latest == null && !probe.IsCurrent(old, TimeSpan.FromDays(1)), "Never retain green data after " + mutation.Name);
        }
        foreach (string operation in new[] { "Mode", "Bus", "Slaves", "Axes", "Type", "Node", "Scale", "Drive", "Alarm", "Position", "Speed" })
        {
            var bad = new FakeReadOnlyDmc { FailOperation = operation };
            using var probe = new CardProbe(bad, config); probe.Open();
            Throws<DiagnosticException>(() => probe.Sample(), "Check SDK return for " + operation);
        }
        {
            var bad = new FakeReadOnlyDmc(); using var probe = new CardProbe(bad, config); probe.Open();
            int nodes = 0; bad.OnCall = name => { if (name == "Node" && ++nodes == 2) bad.Node = new(1002, 0); };
            Throws<DiagnosticException>(() => probe.Sample(), "Detect mapping change within sequential sample");
        }
        {
            var bad = new FakeReadOnlyDmc(); using var probe = new CardProbe(bad, config); probe.Open();
            int reads = 0; bad.OnCall = name => { if (name == "Bus" && ++reads == 2) bad.Bus = 9; };
            Throws<DiagnosticException>(() => probe.Sample(), "Detect bus failure at end of sample");
        }
        {
            var bad = new FakeReadOnlyDmc(); var slowClock = new ManualClock();
            using var probe = new CardProbe(bad, config, slowClock); probe.Open();
            bad.OnCall = name => { if (name == "Position") slowClock.Advance(TimeSpan.FromSeconds(1)); };
            Throws<DiagnosticException>(() => probe.Sample(), "Late synchronous call rejected AFTER it returns, not magically interrupted");
        }
        {
            var bad = new FakeReadOnlyDmc(); using var cancelled = new CancellationTokenSource();
            using var probe = new CardProbe(bad, config); probe.Open();
            bad.OnCall = name => { if (name == "Position") cancelled.Cancel(); };
            Throws<OperationCanceledException>(() => probe.Sample(cancelled.Token), "Cancellation between reads aborts publication");
            Check(!bad.Calls.Contains("Speed") && probe.Latest == null, "No abandoned I/O task or later read after cancellation");
        }
        {
            var bad = new FakeReadOnlyDmc(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            using var probe = new CardProbe(bad, config);
            Throws<OperationCanceledException>(() => probe.Open(cancelled.Token), "Pre-cancelled open makes no initialization");
            Check(bad.Calls.Count == 0, "No cleanup without initialization attempt");
        }
        {
            var bad = new FakeReadOnlyDmc { CloseResult = 5 }; var probe = new CardProbe(bad, config); probe.Open();
            var previous = probe.Sample();
            Throws<DiagnosticException>(probe.Close, "Close failure reported separately");
            Check(!probe.IsCurrent(previous, TimeSpan.FromDays(1)), "Close failure still invalidates observations");
            Throws<InvalidOperationException>(() => probe.Open(), "No automatic reinit after uncertain resource cleanup");
            probe.Dispose(); Check(bad.Calls.Count(c => c == "Close") == 1, "Uncertain global close is not blindly retried");
            Throws<ObjectDisposedException>(() => probe.Sample(), "Disposed probe refuses reuse");
        }
        {
            var bad = new FakeReadOnlyDmc { InitResult = 0, CloseResult = 5 }; using var probe = new CardProbe(bad, config);
            var error = Throws<AggregateException>(() => probe.Open(), "Preserve initialization plus cleanup errors");
            Check(error.InnerExceptions.Count == 2, "Cleanup does not erase primary error");
        }
        string[] allowed = ["Init", "Cards", "Mode", "Bus", "Slaves", "Axes", "Type", "Node", "Drive", "Alarm", "Scale", "Position", "Speed", "Close"];
        Check(api.Calls.All(allowed.Contains), "Exercised calls stay within initialization/read/close port");
        Check(!typeof(IReadOnlyDmc).GetMethods().Any(m => new[] { "enable", "move", "reset", "clear", "write", "download" }.Any(x => m.Name.Contains(x, StringComparison.OrdinalIgnoreCase))), "No motion/configuration/reset commands on interface");
        Console.WriteLine($"Completed {_count} checks. Test-double application logic ONLY; no native SDK or hardware accessed.");
    }
}
