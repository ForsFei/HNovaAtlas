namespace IndustrialMotion;

internal static class ObservationChecks
{
    private static int _count;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, label); return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + label);
    }
    private static MotionAttempt Attempt(ObservedMotionKind kind = ObservedMotionKind.Move, bool feedback = false,
        ObservedAxis? baseline = null, ObservationPolicy? policy = null)
        => new(kind, new(1, 1), baseline ?? new(0), 0, kind == ObservedMotionKind.Move ? 10 : null, feedback, policy);
    private static void Feed(MotionAttempt observer, ObservedAxis sample) => observer.Observe(sample, sample.SampledAt);
    private static void Start(MotionAttempt observer)
    {
        observer.RecordDispatch(DispatchEvidence.Acknowledged, 0);
        Feed(observer, new(.1, IsStopped: false));
    }
    public static void Demo()
    {
        var home = Attempt(ObservedMotionKind.Home, baseline: new(0, HomeResult: 1));
        Start(home); Feed(home, new(.2, HomeResult: 1)); Feed(home, new(.31, HomeResult: 1));
        Console.WriteLine($"SCRIPTED HOME: {home.State}, reference={home.ReferenceEstablished}, evidence={home.Evidence}");
        var move = Attempt(feedback: true); Start(move);
        Feed(move, new(.2, CommandPosition: 10, FeedbackPosition: 9.9, InPosition: false));
        Console.WriteLine($"SCRIPTED MOVE before feedback: {move.State}");
        Feed(move, new(.3, CommandPosition: 10, FeedbackPosition: 10, InPosition: true));
        Feed(move, new(.41, CommandPosition: 10, FeedbackPosition: 10, InPosition: true));
        Console.WriteLine($"SCRIPTED MOVE: {move.State}, evidence={move.Evidence}");
        var missed = Attempt(); missed.RecordDispatch(DispatchEvidence.Acknowledged, 0);
        foreach (double t in new[] { .2, .4, .51 }) Feed(missed, new(t, CommandPosition: 10));
        Console.WriteLine($"MISSED START: {missed.State}, reason={missed.Reason}");
        Console.WriteLine("Scripted observations only; no SDK call, command dispatch or physical stop.");
    }
    public static void Run()
    {
        Throws<ArgumentException>(() => new MotionAttempt(ObservedMotionKind.Move, new(1, 1), new(0), 0), "Move target must be explicit");
        Throws<ArgumentException>(() => new MotionAttempt(ObservedMotionKind.Move, new(1, 0), new(0), 0, 10), "Zero request sequence refused");
        Throws<ArgumentException>(() => new MotionAttempt(ObservedMotionKind.Home, new(1, 1), new(0), 1), "Stale baseline refused before dispatch");
        Throws<ArgumentOutOfRangeException>(() => Attempt(policy: new(Tolerance: double.NaN)), "Nonfinite observation policy refused");
        var unsubmitted = Attempt(); unsubmitted.RecordDispatch(DispatchEvidence.NotAttempted, 0);
        Check(unsubmitted.State == AttemptState.NotSubmitted, "Not attempted stays distinct from unknown outcome");
        var uncertain = Attempt(); uncertain.RecordDispatch(DispatchEvidence.OutcomeUnknown, 0);
        Feed(uncertain, new(.1, CommandPosition: 10));
        Check(uncertain.State == AttemptState.Unknown, "Ambiguous dispatch is not repaired by matching position");
        var slowDispatch = Attempt(); slowDispatch.RecordDispatch(DispatchEvidence.Acknowledged, .3);
        Check(slowDispatch.State == AttemptState.Unknown, "Baseline expiration during call does not become not-submitted");
        var move = Attempt(feedback: true);
        Throws<InvalidOperationException>(() => Feed(move, new(.1)), "No observations before dispatch evidence");
        Start(move);
        Throws<InvalidOperationException>(() => move.RecordDispatch(DispatchEvidence.Acknowledged, .1), "No second dispatch recording");
        Feed(move, new(.2, CommandPosition: 10, FeedbackPosition: 9.9, InPosition: true));
        Check(move.State == AttemptState.Settling, "Stopped command alone cannot satisfy feedback-required completion");
        Feed(move, new(.3, CommandPosition: 10, FeedbackPosition: 10, InPosition: true));
        Check(!move.Terminal, "One matching sample is not a settling interval");
        Feed(move, new(.41, CommandPosition: 10, FeedbackPosition: 10, InPosition: true));
        Check(move.State == AttemptState.Completed && move.Evidence == AttemptEvidence.FeedbackAndInPosition, "Repeated matching observations produce explicit feedback evidence");
        Feed(move, new(.5, DataValid: false));
        Check(move.State == AttemptState.Completed, "Historical outcome is not overwritten by later telemetry; not a live permissive");
        var commandOnly = Attempt(); Start(commandOnly);
        Feed(commandOnly, new(.2, CommandPosition: 10)); Feed(commandOnly, new(.31, CommandPosition: 10));
        Check(commandOnly.State == AttemptState.Completed && commandOnly.Evidence == AttemptEvidence.CommandPositionOnly, "No feedback claim when none was required");
        var missingFeedback = Attempt(feedback: true); Start(missingFeedback);
        Feed(missingFeedback, new(.2, CommandPosition: 10)); Feed(missingFeedback, new(.4, CommandPosition: 10));
        Check(!missingFeedback.Terminal, "Missing feedback is not silently replaced by command position");
        var home = Attempt(ObservedMotionKind.Home, baseline: new(0, HomeResult: 1));
        home.RecordDispatch(DispatchEvidence.Acknowledged, 0); Feed(home, new(.1, HomeResult: 1));
        Check(!home.ReferenceEstablished, "Stale home-done from baseline cannot establish new reference");
        Feed(home, new(.2, IsStopped: false, HomeResult: 0)); Feed(home, new(.3, HomeResult: 1)); Feed(home, new(.41, HomeResult: 1));
        Check(home.ReferenceEstablished && home.Evidence == AttemptEvidence.HomeResultTransition, "Home requires post-dispatch motion and incomplete-to-done evidence");
        var staleHome = Attempt(ObservedMotionKind.Home, baseline: new(0, HomeResult: 1));
        staleHome.RecordDispatch(DispatchEvidence.Acknowledged, 0); Feed(staleHome, new(.1, IsStopped: false, HomeResult: 1));
        Feed(staleHome, new(.2, HomeResult: 1)); Feed(staleHome, new(.4, HomeResult: 1));
        Check(!staleHome.ReferenceEstablished, "Busy alone does not freshen an unchanged home-done bit");
        var missed = Attempt(); missed.RecordDispatch(DispatchEvidence.Acknowledged, 0);
        foreach (double t in new[] { .2, .4, .51 }) Feed(missed, new(t, CommandPosition: 10));
        Check(missed.State == AttemptState.Unknown && missed.Reason.StartsWith("StartNotObserved"), "Fast/unobserved start is unknown, not assumed rejected or complete");
        var interruptedHome = Attempt(ObservedMotionKind.Home); Start(interruptedHome); interruptedHome.Withdraw();
        Feed(interruptedHome, new(.2, HomeResult: 1));
        Check(interruptedHome.State == AttemptState.Unknown && !interruptedHome.ReferenceEstablished, "Withdrawn home cannot later be promoted to success");
        var neverSent = Attempt(); neverSent.Withdraw();
        Check(neverSent.State == AttemptState.NotSubmitted, "Withdrawing a prepared-only observer made no submission");
        var stop = Attempt(ObservedMotionKind.Stop, baseline: new(0, IsStopped: false)); Start(stop);
        Feed(stop, new(.2, StopReason: 14)); Feed(stop, new(.31, StopReason: 14));
        Check(stop.State == AttemptState.Stopped && stop.Evidence == AttemptEvidence.ObservedStandstill && !stop.ReferenceEstablished,
            "Stop result is standstill observation, never original target completion or home reference");
        var alarmStop = Attempt(ObservedMotionKind.Stop); Start(alarmStop); Feed(alarmStop, new(.2, StopReason: 5));
        Check(alarmStop.State == AttemptState.Failed && alarmStop.Reason.Contains("5"), "Limit stop is not a normal stop-request result");
        foreach (int code in new[] { 1, 4, 5, 9, 13, 14, 24, 999 })
        {
            var abnormal = Attempt(); Start(abnormal); Feed(abnormal, new(.2, StopReason: code, CommandPosition: 10));
            Check(abnormal.State == AttemptState.Failed, $"Move never completes after stop reason {code}");
        }
        ObservedAxis[] invalid =
        [new(.2, DataValid: false), new(.2, ConnectionGeneration: 2), new(.2, BusError: 9),
            new(.05), new(.5), new(.2, HomeResult: 9), new(.2, CommandPosition: double.NaN), new(.2, FeedbackPosition: double.PositiveInfinity)];
        foreach (var sample in invalid)
        {
            var observer = Attempt(); Start(observer); observer.Observe(sample, .2);
            Check(observer.State == AttemptState.Unknown, "Bad quality/epoch/order/time/value cannot complete");
        }
        foreach (var sample in new[] { new ObservedAxis(.2, AxisError: 7), new(.2, LimitActive: true), new(.2, DriveState: 7), new(.2, DriveState: 1) })
        {
            var observer = Attempt(); Start(observer); Feed(observer, sample);
            Check(observer.State == AttemptState.Failed, "Drive/limit/enable fault is a failed attempt, not stop proof");
        }
        var age = Attempt(); Start(age); age.Observe(new(.2), .5);
        Check(age.State == AttemptState.Unknown, "Old sample age invalidates evidence despite increasing sample time");
        var gap = Attempt(); Start(gap); Feed(gap, new(.4));
        Check(gap.State == AttemptState.Unknown, "Fresh sample after an observation gap does not hide missing evidence");
        var lost = Attempt(); Start(lost); lost.CheckDeadline(.36);
        Check(lost.State == AttemptState.Unknown && lost.Reason.StartsWith("ObservationLost"), "Missing polling observations are not a stop command");
        var deadline = Attempt(policy: new(StartTimeout: .5, TotalTimeout: .6)); Start(deadline);
        Feed(deadline, new(.3, IsStopped: false)); Feed(deadline, new(.5, IsStopped: false)); Feed(deadline, new(.61, IsStopped: false));
        Check(deadline.State == AttemptState.Unknown && deadline.Reason.StartsWith("ObservationTimeout"), "Total timeout while moving is unknown and does not stop the axis");
        var bounce = Attempt(); Start(bounce); Feed(bounce, new(.2, CommandPosition: 10)); Feed(bounce, new(.25, IsStopped: false));
        Feed(bounce, new(.3, CommandPosition: 10)); Feed(bounce, new(.35, CommandPosition: 10));
        Check(!bounce.Terminal, "Renewed motion resets settling evidence");
        Feed(bounce, new(.41, CommandPosition: 10)); Check(bounce.State == AttemptState.Completed, "Settling can complete after a fresh repeated interval");
        var backwards = Attempt(); Start(backwards); backwards.CheckDeadline(.05);
        Check(backwards.State == AttemptState.Unknown, "Clock-domain regression is not ignored");
        using (var axis = new SimAxis(initialPosition: 1))
        {
            axis.Open(); axis.Enable(); double time = 0;
            ObservedAxis SampleModel()
            {
                AxisSnapshot snapshot = axis.Read();
                // ONLY this ideal simulator has this mapping; not a real SDK adapter.
                return new(time, snapshot.Key.ConnectionGeneration, snapshot.DataValid,
                    DriveState: snapshot.Enabled ? (ushort)4 : (ushort)1, IsStopped: !snapshot.Busy,
                    HomeResult: snapshot.Homed ? (ushort)1 : (ushort)0, CommandPosition: snapshot.CommandPosition,
                    FeedbackPosition: snapshot.FeedbackPosition, InPosition: true);
            }
            var observedHome = new MotionAttempt(ObservedMotionKind.Home, new(1, 1), SampleModel(), time);
            axis.Home(); observedHome.RecordDispatch(DispatchEvidence.Acknowledged, time);
            for (int i = 0; i < 500 && !observedHome.Terminal; i++)
            { axis.Tick(.01); time += .01; observedHome.Observe(SampleModel(), time); }
            Check(observedHome.ReferenceEstablished, "Observe home through existing pure SimAxis, not only hand-scripted states");
            var observedMove = new MotionAttempt(ObservedMotionKind.Move, new(1, 2), SampleModel(), time, 1, true);
            axis.MoveAbsolute(1, 5); observedMove.RecordDispatch(DispatchEvidence.Acknowledged, time);
            for (int i = 0; i < 500 && !observedMove.Terminal; i++)
            { axis.Tick(.01); time += .01; observedMove.Observe(SampleModel(), time); }
            Check(observedMove.State == AttemptState.Completed && observedMove.Evidence == AttemptEvidence.FeedbackAndInPosition,
                "Observe ideal simulated move and feedback with the same completion policy");
        }
        Console.WriteLine($"Completed {_count} observation checks. Scripted inputs only; no SDK commands or hardware accessed.");
    }
}
