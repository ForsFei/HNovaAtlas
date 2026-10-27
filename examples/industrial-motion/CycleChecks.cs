namespace IndustrialMotion;

internal static class CycleChecks
{
    private static int _count;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_count}: {label}");
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + label);
    }
    private static PositioningCycle Started()
    {
        var cycle = new PositioningCycle(); cycle.Start(new(0), 0, true); return cycle;
    }
    private static CycleTicket Ticket(PositioningCycle cycle, CycleAction action) => cycle.Pending.Single(t => t.Action == action);
    private static void Done(PositioningCycle cycle, CycleAction action, double time) =>
        cycle.Report(Ticket(cycle, action), CycleResult.Completed, new(time), time);
    private static PositioningCycle Waiting()
    {
        var cycle = Started();
        Done(cycle, CycleAction.RetractZ, .01);
        Done(cycle, CycleAction.MoveX, .02); Done(cycle, CycleAction.MoveY, .03);
        Done(cycle, CycleAction.ApproachZ, .04); Done(cycle, CycleAction.IndicatorOn, .05);
        return cycle;
    }
    public static void Demo()
    {
        var cycle = Started(); double now = 0;
        while (cycle.Pending.Count > 0)
        {
            CycleTicket ticket = cycle.Pending[0]; now += .01;
            Console.WriteLine($"Fixture intent: {ticket.Action}; inject qualified completion (no SDK call)");
            cycle.Report(ticket, CycleResult.Completed, new(now), now);
            Console.WriteLine($"Stage={cycle.Stage}, pending={cycle.Pending.Count}");
        }
        cycle.Observe(new(now += .01, Acknowledgement: true), now);
        Console.WriteLine($"Old high: {cycle.Stage}");
        cycle.Observe(new(now += .01, Acknowledgement: false), now);
        cycle.Observe(new(now += .01, Acknowledgement: true), now);
        Console.WriteLine($"Fresh low/high: {cycle.Stage}. IndicatorRequested={cycle.IndicatorRequested} is intent, NOT physical output.");
    }
    public static void Run()
    {
        var cycle = new PositioningCycle();
        Check(cycle.Stage == CycleStage.Idle && cycle.Pending.Count == 0, "No intent before explicit start");
        cycle.Observe(new(0), 0); cycle.Withdraw();
        Check(cycle.Stage == CycleStage.Idle, "Idle observation/withdrawal cannot auto-start");
        cycle.Start(new(0), 0, true);
        Check(cycle.Stage == CycleStage.RetractZ && cycle.Pending.Single().Action == CycleAction.RetractZ, "Only Z retract released first");
        Check(!cycle.IndicatorRequested, "No indicator intent before positioning");
        Throws<InvalidOperationException>(() => cycle.Start(new(.01), .01, true), "Repeated start refused");
        var oldZ = cycle.Pending.Single();
        Done(cycle, CycleAction.RetractZ, .01);
        Check(cycle.Stage == CycleStage.PositionXY && cycle.Pending.Count == 2, "XY intents only after qualified Z completion");
        Check(cycle.Pending.All(t => t.Action is CycleAction.MoveX or CycleAction.MoveY), "No approach or output mixed into XY group");
        cycle.Report(oldZ, CycleResult.Completed, new(9, DataValid: false), 9);
        Check(cycle.Stage == CycleStage.PositionXY, "Late prior-stage result ignored, cannot refresh/poison current batch");
        var x = Ticket(cycle, CycleAction.MoveX); var y = Ticket(cycle, CycleAction.MoveY);
        cycle.Report(x, CycleResult.Completed, new(.02), .02);
        Check(cycle.Stage == CycleStage.PositionXY && cycle.Pending.SequenceEqual(new[] { y }), "X complete does not imply Y complete");
        cycle.Report(x, CycleResult.Unknown, new(.021), .021);
        Check(cycle.Stage == CycleStage.PositionXY, "Duplicate X result ignored");
        cycle.Report(y, CycleResult.Completed, new(.03), .03);
        Check(cycle.Stage == CycleStage.ApproachZ, "Both distinct XY reports required before Z approach");
        Done(cycle, CycleAction.ApproachZ, .04);
        Check(cycle.Stage == CycleStage.Indicator && cycle.IndicatorRequested, "Output intent after approach, not physical confirmation");
        Done(cycle, CycleAction.IndicatorOn, .05);
        Check(cycle.Stage == CycleStage.WaitAck && cycle.Pending.Count == 0, "Output readback report enters acknowledgement stage");
        cycle.Observe(new(.06, Acknowledgement: true), .06);
        Check(cycle.Stage == CycleStage.WaitAck, "Initially high acknowledgement cannot complete new cycle");
        cycle.Observe(new(.07, Acknowledgement: false), .07);
        Check(cycle.Stage == CycleStage.WaitAck, "Low only arms fresh edge");
        cycle.Observe(new(.08, Acknowledgement: true), .08);
        Check(cycle.Stage == CycleStage.Completed, "Fresh post-entry low/high completes fixture");
        cycle.Observe(new(99, DataValid: false), 99); cycle.CheckDeadline(99); cycle.Withdraw();
        Check(cycle.Stage == CycleStage.Completed && cycle.IndicatorRequested, "Historical completion retained; no automatic output reset");
        Check(!cycle.RequiresStopReview, "Successful history is not a current health/stop permission");
        var foreign = Started(); var a = Started();
        a.Report(foreign.Pending.Single(), CycleResult.Completed, new(.01), .01);
        Check(a.Stage == CycleStage.RetractZ, "Cross-instance result cannot advance same named action");
        a = Started(); Done(a, CycleAction.RetractZ, .01); Done(a, CycleAction.MoveY, .02);
        Check(a.Stage == CycleStage.PositionXY && a.Pending.Single().Action == CycleAction.MoveX, "Either XY report order supported");
        foreach (var result in new[] { CycleResult.Rejected, CycleResult.Stopped, CycleResult.Unknown })
        {
            a = Started(); Done(a, CycleAction.RetractZ, .01); Done(a, CycleAction.MoveX, .02);
            a.Report(Ticket(a, CycleAction.MoveY), result, new(.03), .03);
            Check(a.Stage == CycleStage.Halted && a.Pending.Count == 0 && a.RequiresStopReview, "Partial XY " + result + " prevents approach; no rollback/stop fabricated");
        }
        a = Started(); a.Withdraw();
        Check(a.Stage == CycleStage.Halted && a.Reason == "WithdrawnNotAStop", "Withdrawal blocks next intents, does not claim stop");
        a.Observe(new(.1), .1);
        Check(a.Stage == CycleStage.Halted, "Health recovery cannot resume halted instance");
        Throws<InvalidOperationException>(() => a.Start(new(.11), .11, true), "Halted cycle cannot replay by restart");
        a = Waiting(); a.Observe(new(.06, Acknowledgement: false), .06);
        a.Observe(new(.07, Acknowledgement: null), .07); a.Observe(new(.08, Acknowledgement: true), .08);
        Check(a.Stage == CycleStage.WaitAck, "Unknown input breaks low/high qualification");
        a.Observe(new(.09, Acknowledgement: false), .09); a.Observe(new(.1, Acknowledgement: true), .1);
        Check(a.Stage == CycleStage.Completed, "New low/high after unknown is accepted");
        a = Started(); a.Observe(new(.01, Acknowledgement: false), .01);
        Done(a, CycleAction.RetractZ, .02); Done(a, CycleAction.MoveX, .03); Done(a, CycleAction.MoveY, .04);
        Done(a, CycleAction.ApproachZ, .05); Done(a, CycleAction.IndicatorOn, .06);
        a.Observe(new(.07, Acknowledgement: true), .07);
        Check(a.Stage == CycleStage.WaitAck, "Low observed before wait stage does not arm new acknowledgement");
        foreach (var bad in new[] { new CycleFrame(.01, DataValid: false), new(.01, InterlocksOk: false),
            new(.01, Generation: 2), new(.01, Generation: 0), new(.02), new(double.NaN), new(-1), new(0) })
        {
            a = Started(); a.Observe(bad, .01);
            Check(a.Stage == CycleStage.Halted, "Invalid frame rejected: " + bad);
        }
        a = Started(); a.Observe(new(0), .3);
        Check(a.Stage == CycleStage.Halted, "Old data not renewed by current receipt time");
        a = Started(); a.Observe(new(.3), .3);
        Check(a.Stage == CycleStage.Halted, "Fresh sample after oversized observation gap is insufficient");
        a = Started(); a.CheckDeadline(.26);
        Check(a.Stage == CycleStage.Halted && a.Reason == "ObservationLostNotAStop", "Deadline check detects absent samples without pretending stop");
        a = Started(); a.Observe(new(.01), .01); a.CheckDeadline(.005);
        Check(a.Stage == CycleStage.Halted && a.Reason == "InvalidMonotonicTime", "Backwards clock rejected");
        a = Started(); a.CheckDeadline(double.PositiveInfinity);
        Check(a.Stage == CycleStage.Halted, "Non-finite clock rejected");
        a = new PositioningCycle(stepTimeout: .5); a.Start(new(0), 0, true);
        a.Observe(new(.2), .2); a.Observe(new(.4), .4);
        a.Report(a.Pending.Single(), CycleResult.Completed, new(.5), .5);
        Check(a.Stage == CycleStage.Halted && a.Reason == "StepTimeoutNotAStop", "Late success at timeout boundary cannot advance");
        a = Waiting(); a.Withdraw();
        Check(a.IndicatorRequested && a.RequiresStopReview, "Aborting does not invent a safe output level");
        a = new(); a.Start(new(0), 0, false);
        Check(a.Stage == CycleStage.Halted && a.Pending.Count == 0, "Unreviewed start releases no instructions");
        a = new(); a.Start(new(0, Generation: 0), 0, true);
        Check(a.Stage == CycleStage.Halted, "Zero initial generation refused");
        a = new(); a.Start(new(0), -.1, true);
        Check(a.Stage == CycleStage.Halted, "Negative initial clock refused");
        Throws<ArgumentOutOfRangeException>(() => new PositioningCycle(maxAge: 0), "Invalid freshness policy refused");
        Throws<ArgumentOutOfRangeException>(() => new PositioningCycle(maxGap: double.NaN), "Invalid gap policy refused");
        Throws<ArgumentOutOfRangeException>(() => new PositioningCycle(stepTimeout: .25), "Timeout must exceed sample gap");
        a = Started();
        Throws<ArgumentOutOfRangeException>(() => a.Report(a.Pending.Single(), (CycleResult)99, new(.01), .01), "Undefined result is not success");
        Console.WriteLine($"{_count} offline cycle checks passed. No SDK, axes, output or physical safety acceptance.");
    }
}
