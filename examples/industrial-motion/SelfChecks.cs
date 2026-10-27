namespace IndustrialMotion;

internal static class SelfChecks
{
    private static int _checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine($"PASS {++_checks}: {label}");
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, label); return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}: {label}");
    }
    private static void Finish(SimAxis axis, int maxTicks = 4000)
    {
        for (int i = 0; axis.Read().Busy && i < maxTicks; i++) axis.Tick(.01);
        if (axis.Read().Busy) throw new InvalidOperationException("Test simulation-time watchdog expired.");
    }
    private static SimAxis Homed(AxisSettings? settings = null)
    {
        var axis = new SimAxis(settings); axis.Open(); axis.Enable(); axis.Home(); Finish(axis);
        if (!axis.Read().Homed) throw new InvalidOperationException("Fixture did not home.");
        return axis;
    }

    public static void Run()
    {
        var scale = new UnitScale(4000);
        Check(scale.ToCount(12.5m) == 50000 && scale.ToMillimetres(50000) == 12.5m, "Explicit counts/mm round trip");
        Check(scale.ToCount(.000125m) == 1 && scale.ToCount(-.000125m) == -1, "Symmetric away-from-zero midpoint rounding");
        Throws<OverflowException>(() => scale.ToCount(1_000_000m), "Int32 count overflow is not silently truncated");
        Throws<ArgumentOutOfRangeException>(() => new UnitScale(0), "Nonpositive scale rejected");
        var longPlan = new TrapezoidPlan(25, 10, 100, 100);
        Check(!longPlan.IsTriangular && Math.Abs(longPlan.Duration - 2.6) < 1e-9, "Long move reaches trapezoid cruise");
        var shortPlan = new TrapezoidPlan(.1, 10, 100, 100);
        Check(shortPlan.IsTriangular && shortPlan.PeakVelocity < 10, "Short move is triangular, never assumes cruise speed");
        var asymmetric = new TrapezoidPlan(5, 4, 8, 2);
        double previous = 0;
        bool valid = true;
        for (int i = 0; i <= 1000; i++)
        {
            var sample = asymmetric.Sample(asymmetric.Duration * i / 1000);
            valid &= sample.Distance >= previous - 1e-10 && sample.Distance <= 5 + 1e-10 && sample.Velocity >= 0 && sample.Velocity <= 4;
            previous = sample.Distance;
        }
        Check(valid && asymmetric.Sample(asymmetric.Duration) == (5, 0), "Asymmetric profile monotonic, bounded, exact endpoint");
        Throws<ArgumentOutOfRangeException>(() => new TrapezoidPlan(1, double.NaN, 1, 1), "NaN profile rejected");
        Throws<ArgumentOutOfRangeException>(() => new SimAxis(new AxisSettings(MaxVelocity: double.PositiveInfinity)), "Nonfinite axis settings rejected");

        using (var axis = new SimAxis())
        {
            Check(!axis.Read().DataValid && axis.Read().State == AxisState.Disconnected, "Closed data is not valid");
            Throws<InvalidOperationException>(axis.Enable, "Enable requires session");
            axis.Open();
            Check(axis.Read().DataValid && !axis.Read().Enabled && !axis.Read().Homed, "Open is not enabled or homed");
            axis.Enable();
            Throws<InvalidOperationException>(() => axis.MoveAbsolute(10, 5), "Position move requires homing");
            CommandKey home = axis.Home();
            axis.Tick(.01);
            Check(axis.Read().State == AxisState.Homing && !axis.Read().Homed, "Home is active before sensor event");
            Throws<InvalidOperationException>(() => axis.MoveAbsolute(10, 5), "Busy axis refuses competing motion");
            Finish(axis);
            Check(axis.Read().CompletedFor(home) && axis.Read().Homed && axis.Read().CommandPosition == 0, "Simulated sensor establishes reference");
            CommandKey move = axis.MoveAbsolute(25, 10);
            Check(!axis.Read().CompletedFor(home) && axis.Read().Result == CommandResult.Active, "Previous Done cannot satisfy a new command");
            axis.Tick(.01);
            Check(axis.Read().Velocity is > 0 and < 10, "Point move accelerates from rest");
            Finish(axis);
            Check(axis.Read().CompletedFor(move) && Math.Abs(axis.Read().CommandPosition - 25) < 1e-9 && axis.Read().Evidence == CompletionEvidence.SimulatedFeedback,
                "Matching move completes with explicit simulated feedback evidence");
            axis.SetWorkOffset(10);
            Check(axis.Read().CommandPosition == 25 && axis.Read().WorkPosition == 15, "Work offset does not move machine coordinate");
            CommandKey relative = axis.MoveRelative(-5, 5); Finish(axis);
            Check(axis.Read().CompletedFor(relative) && Math.Abs(axis.Read().CommandPosition - 20) < 1e-9 && Math.Abs(axis.Read().WorkPosition - 10) < 1e-9,
                "Relative move uses current machine coordinate, no double offset");
            Throws<ArgumentOutOfRangeException>(() => axis.MoveAbsolute(101, 1), "Target outside configured travel rejected before start");
            Throws<ArgumentOutOfRangeException>(() => axis.MoveAbsolute(double.NaN, 1), "NaN target rejected");
            Throws<ArgumentOutOfRangeException>(() => axis.MoveAbsolute(30, 21), "Excess velocity rejected");
            CommandKey interrupted = axis.MoveAbsolute(80, 10);
            axis.Tick(.1);
            Throws<InvalidOperationException>(() => axis.SetWorkOffset(1), "Coordinate edits prohibited while busy");
            Throws<InvalidOperationException>(axis.Disable, "Disable is not used as a motion stop");
            Throws<InvalidOperationException>(axis.Close, "Close refuses active motion");
            double beforeStop = axis.Read().CommandPosition;
            axis.RequestStop();
            Check(axis.Read().State == AxisState.Stopping && axis.Read().Result == CommandResult.Active, "Stop request awaits standstill");
            Finish(axis);
            Check(axis.Read().Result == CommandResult.Stopped && !axis.Read().CompletedFor(interrupted) && axis.Read().CommandPosition > beforeStop,
                "Controlled stop travels a deceleration distance and never reports target Done");
            Check(axis.Read().Homed, "Controlled normal stop retains model reference");
            axis.Disable(); axis.Close();
            Check(!axis.Read().DataValid && !axis.Read().Homed, "Close invalidates reference and displayed data");
            axis.Open(); axis.Enable();
            Check(axis.Read().Key.ConnectionGeneration != move.ConnectionGeneration && !axis.Read().CompletedFor(move), "Reopen isolates old command identity");
        }
        using (var axis = new SimAxis(new AxisSettings(HomeTimeout: .1)))
        {
            axis.Open(); axis.Enable(); axis.Home(); Finish(axis);
            Check(axis.Read().Fault == "HomeTimeout" && !axis.Read().Homed, "Home timeout does not set Homed");
        }
        using (var axis = new SimAxis())
        {
            axis.Open(); axis.Enable(); axis.SetInputs(new(HomeSensorAvailable: false)); axis.Home(); Finish(axis);
            Check(axis.Read().State == AxisState.Faulted && !axis.Read().Homed, "Missing home sensor cannot fabricate a zero");
        }
        using (var axis = new SimAxis())
        {
            axis.Open(); axis.Enable(); axis.Home(); axis.Tick(.1); axis.RequestStop(); Finish(axis);
            Check(axis.Read().Result == CommandResult.Stopped && !axis.Read().Homed, "Interrupted home is not referenced");
        }
        using (var axis = new SimAxis(initialPosition: 0))
        {
            axis.Open(); axis.Enable();
            Throws<InvalidOperationException>(() => axis.Home(), "Simplified home refuses initially active sensor, does not fake full homing modes");
        }
        using (var axis = Homed())
        {
            CommandKey jog = axis.Jog(1, 1);
            axis.Tick(.1); axis.RenewJog(jog); axis.Tick(.1);
            Check(axis.Read().State == AxisState.Jogging, "Matching jog lease renewed");
            axis.Tick(.1); axis.Tick(.1); Finish(axis);
            Check(axis.Read().Result == CommandResult.Stopped && axis.Read().State == AxisState.Ready, "Expired simulated jog lease requests controlled stop");
            Throws<InvalidOperationException>(() => axis.RenewJog(jog), "Expired jog cannot be revived by stale renew");
            Throws<ArgumentOutOfRangeException>(() => axis.Jog(1, 3), "Manual jog speed cap enforced");
        }
        using (var axis = Homed())
        {
            axis.MoveAbsolute(10, 5); axis.InjectFollowingOffset(1); axis.Tick(.01);
            Check(axis.Read().Fault == "FollowingError" && axis.Read().Result == CommandResult.Faulted && !axis.Read().Homed,
                "Excess following error is not completion");
            Throws<InvalidOperationException>(axis.ResetFault, "Reset blocked while discrepancy remains");
            axis.InjectFollowingOffset(0); axis.ResetFault();
            Check(axis.Read().State == AxisState.Disabled && !axis.Read().Enabled && !axis.Read().Homed && axis.Read().Result == CommandResult.Faulted,
                "Reset neither enables, rehomes, replays nor changes old failure to Done");
        }
        using (var axis = Homed(new AxisSettings(MoveTimeout: .2)))
        {
            axis.SetInputs(new(InPosition: false)); axis.MoveAbsolute(.01, 1); Finish(axis);
            Check(axis.Read().Fault == "MoveTimeout" && axis.Read().Result != CommandResult.Completed, "Stopped planner without in-position feedback does not complete");
        }
        using (var axis = Homed(new AxisSettings(HasFeedback: false)))
        {
            axis.MoveAbsolute(1, 1); Finish(axis);
            Check(axis.Read().FeedbackPosition == null && axis.Read().Evidence == CompletionEvidence.CommandOnly, "No encoder: report command-only evidence, not fabricated feedback");
        }
        foreach (var fault in new[] { new AxisInputs(Alarm: true), new AxisInputs(Emergency: true), new AxisInputs(PositiveLimit: true),
            new AxisInputs(NegativeLimit: true), new AxisInputs(DriveReady: false), new AxisInputs(BusOperational: false) })
        {
            using var axis = Homed(); axis.MoveAbsolute(10, 5); axis.SetInputs(fault);
            Check(axis.Read().State == AxisState.Faulted && !axis.Read().Enabled && !axis.Read().Homed && axis.Read().Result != CommandResult.Completed,
                "Input fault blocks motion: " + axis.Read().Fault);
            Throws<InvalidOperationException>(axis.ResetFault, "Fault cannot be reset while input persists");
            if (!fault.BusOperational)
                Check(!axis.Read().DataValid && axis.Read().Result == CommandResult.Uncertain, "Bus loss invalidates feedback and physical outcome");
            axis.SetInputs(new()); axis.ResetFault();
            Check(axis.Read().State == AxisState.Disabled && !axis.Read().Homed, "Restored input does not auto-enable or auto-home");
        }
        using (var axis = Homed())
        {
            axis.MoveAbsolute(99.99, 20); Finish(axis);
            var jog = axis.Jog(1, 1); axis.Tick(.1);
            Check(axis.Read().Fault == "SoftwareTravelBoundary" && axis.Read().Result != CommandResult.Completed, "Crossed simulated travel bound faults, not a safety-rated stop guarantee");
        }
        using (var axis = Homed())
        {
            Throws<ArgumentOutOfRangeException>(() => axis.Tick(.2), "Unbounded simulation time step refused");
            axis.MoveAbsolute(20, 5);
            axis.Dispose();
            Check(!axis.Read().DataValid && axis.Read().Result == CommandResult.Uncertain, "Dispose while busy gives no stop-completion evidence");
            Throws<ObjectDisposedException>(axis.Open, "Disposed simulator cannot reopen");
        }
        Console.WriteLine($"Completed {_checks} checks. Pure simulation; no Leadshine SDK, DLL, driver or hardware accessed.");
    }
}
