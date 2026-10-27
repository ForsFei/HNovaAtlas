namespace IndustrialRobotics;

public enum RobotMode { Unknown, Teach, Automatic }
public readonly record struct RobotBinding(Guid ControllerBoot, uint Revision, ushort ToolId, ushort WorkFrameId);
public sealed record RobotContextSample(RobotBinding Binding, double SampledAt,
    RobotMode Mode = RobotMode.Unknown, bool Valid = false, bool RemoteSelected = false,
    bool ProgramIdle = false, bool NoFaultObserved = false, bool ProcessPermit = false);
public readonly record struct ContextReview(bool PrerequisitesObserved, string Reason);

// Stateless review of declared ordinary application facts, NOT permission to start a robot.
public static class RobotContextReview
{
    public static ContextReview Assess(RobotBinding expected, RobotContextSample sample,
        double now, double maxAge = .25)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (expected.ControllerBoot == Guid.Empty || expected.Revision == 0)
            throw new ArgumentException("A reconciled expected identity/revision is required.");
        if (!double.IsFinite(now) || now < 0 || !double.IsFinite(maxAge) || maxAge <= 0)
            throw new ArgumentException("Finite nonnegative clock and positive age budget required.");
        if (!sample.Valid || !double.IsFinite(sample.SampledAt) || sample.SampledAt < 0
            || sample.SampledAt > now || now - sample.SampledAt > maxAge)
            return new(false, "Missing, invalid, future or stale ordinary snapshot.");
        if (sample.Binding != expected) return new(false, "Controller or frame/tool binding changed: reconcile.");
        if (sample.Mode != RobotMode.Automatic || !sample.RemoteSelected)
            return new(false, "Automatic mode and remote control ownership not observed.");
        if (!sample.ProgramIdle || !sample.NoFaultObserved || !sample.ProcessPermit)
            return new(false, "Idle/no-fault/process prerequisites not all observed.");
        return new(true, "Declared prerequisites only; no safety, standstill, path or dispatch approval.");
    }
}
