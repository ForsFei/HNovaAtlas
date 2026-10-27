namespace IndustrialMotion;

internal enum AxisState { Disconnected, Disabled, Ready, Homing, Moving, Settling, Jogging, Stopping, Faulted }
internal enum CommandResult { None, Active, Completed, Stopped, Faulted, Uncertain }
internal enum CompletionEvidence { None, CommandOnly, SimulatedFeedback }
internal readonly record struct CommandKey(ulong ConnectionGeneration, ulong Sequence);
internal sealed record AxisInputs(bool BusOperational = true, bool DriveReady = true, bool Alarm = false,
    bool Emergency = false, bool PositiveLimit = false, bool NegativeLimit = false,
    bool InPosition = true, bool HomeSensorAvailable = true);
internal sealed record AxisSettings(double Minimum = -10, double Maximum = 100, double MaxVelocity = 20,
    double Acceleration = 100, double Deceleration = 100, double HomeVelocity = 5,
    double HomeTimeout = 8, double MoveTimeout = 30, double FollowingErrorLimit = .5,
    double InPositionTolerance = .01, double JogVelocityLimit = 2, double JogLease = .25, bool HasFeedback = true)
{
    public void Validate()
    {
        double[] values = [Minimum, Maximum, MaxVelocity, Acceleration, Deceleration, HomeVelocity, HomeTimeout,
            MoveTimeout, FollowingErrorLimit, InPositionTolerance, JogVelocityLimit, JogLease];
        if (values.Any(value => !double.IsFinite(value)) || Minimum >= 0 || Maximum <= 0 ||
            MaxVelocity <= 0 || Acceleration <= 0 || Deceleration <= 0 || HomeVelocity <= 0 || HomeVelocity > MaxVelocity ||
            HomeTimeout <= 0 || MoveTimeout <= 0 || InPositionTolerance <= 0 || FollowingErrorLimit < InPositionTolerance ||
            JogVelocityLimit <= 0 || JogVelocityLimit > MaxVelocity || JogLease <= 0 ||
            Math.Abs(Minimum) > 1e6 || Maximum > 1e6 || MaxVelocity > 1e4 ||
            Acceleration > 1e6 || Deceleration > 1e6 || HomeTimeout > 3600 || MoveTimeout > 3600)
            throw new ArgumentOutOfRangeException(nameof(AxisSettings));
    }
}
internal sealed record AxisSnapshot(CommandKey Key, AxisState State, CommandResult Result, bool DataValid,
    bool Enabled, bool Homed, double CommandPosition, double? FeedbackPosition, double WorkPosition,
    double Velocity, double? Target, string? Fault, CompletionEvidence Evidence)
{
    public bool Busy => State is AxisState.Homing or AxisState.Moving or AxisState.Settling or AxisState.Jogging or AxisState.Stopping;
    public bool CompletedFor(CommandKey key) => DataValid && Key == key && Result == CommandResult.Completed && State == AxisState.Ready;
}

// All calls are serialized by the caller. Tick is SIMULATED time, never an EtherCAT cyclic thread.
internal sealed class SimAxis : IDisposable
{
    private readonly AxisSettings _settings;
    private AxisInputs _inputs = new();
    private AxisState _state = AxisState.Disconnected;
    private CommandResult _result;
    private CompletionEvidence _evidence;
    private bool _connected, _enabled, _homed, _disposed;
    private ulong _generation, _sequence;
    private double _position, _workOffset, _velocity, _followingOffset, _elapsed, _origin, _direction;
    private double _stopVelocity, _leaseAge;
    private double? _target;
    private string? _fault;
    private TrapezoidPlan? _plan;

    public SimAxis(AxisSettings? settings = null, double initialPosition = 20)
    {
        _settings = settings ?? new(); _settings.Validate();
        if (!double.IsFinite(initialPosition) || initialPosition < _settings.Minimum || initialPosition > _settings.Maximum)
            throw new ArgumentOutOfRangeException(nameof(initialPosition));
        _position = initialPosition;
    }

    public AxisSnapshot Read() => new(new(_generation, _sequence), _state, _result, _connected && _inputs.BusOperational,
        _enabled, _homed, _position, _settings.HasFeedback ? _position - _followingOffset : null,
        _position - _workOffset, _velocity, _target, _fault, _evidence);

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected) throw new InvalidOperationException("Already open.");
        _generation = checked(_generation + 1); _connected = true; _enabled = _homed = false;
        _state = AxisState.Disabled; _fault = null; _target = null; _velocity = 0;
        _result = CommandResult.None; _evidence = CompletionEvidence.None;
    }

    private void RequireOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connected) throw new InvalidOperationException("No simulator session.");
    }
    private void Healthy()
    {
        if (!_inputs.BusOperational || !_inputs.DriveReady || _inputs.Alarm || _inputs.Emergency ||
            _inputs.PositiveLimit || _inputs.NegativeLimit) throw new InvalidOperationException("Normalized inputs prohibit operation.");
    }
    private void RequireReady(bool reference = true)
    {
        RequireOpen(); Healthy();
        if (_state != AxisState.Ready || !_enabled || (reference && !_homed))
            throw new InvalidOperationException("Ready/enabled/reference conditions not met.");
    }
    private CommandKey Begin(AxisState state)
    {
        _sequence = checked(_sequence + 1); _state = state; _result = CommandResult.Active;
        _evidence = CompletionEvidence.None; _elapsed = 0; _origin = _position; _fault = null;
        return new(_generation, _sequence);
    }
    public void Enable()
    {
        RequireOpen(); Healthy();
        if (_state != AxisState.Disabled) throw new InvalidOperationException("Enable only from Disabled.");
        _enabled = true; _state = AxisState.Ready; // Simulator instantly acknowledges, unlike a real drive.
    }
    public void Disable()
    {
        RequireOpen();
        if (Read().Busy) throw new InvalidOperationException("Stop and observe standstill first.");
        if (_state == AxisState.Faulted) throw new InvalidOperationException("Resolve fault first.");
        _enabled = false; _homed = false; _state = AxisState.Disabled;
    }
    public CommandKey Home()
    {
        RequireReady(false);
        if (_position <= 0) throw new InvalidOperationException("Simplified home requires start on positive side, sensor initially inactive.");
        _homed = false; _target = null; _velocity = -_settings.HomeVelocity;
        return Begin(AxisState.Homing);
    }
    public CommandKey MoveAbsolute(double target, double velocity)
    {
        RequireReady();
        if (!double.IsFinite(target) || target < _settings.Minimum || target > _settings.Maximum ||
            !double.IsFinite(velocity) || velocity <= 0 || velocity > _settings.MaxVelocity)
            throw new ArgumentOutOfRangeException(nameof(target));
        _direction = Math.Sign(target - _position); _target = target; _velocity = 0;
        _plan = target == _position ? null : new(Math.Abs(target - _position), velocity, _settings.Acceleration, _settings.Deceleration);
        return Begin(_plan == null ? AxisState.Settling : AxisState.Moving);
    }
    public CommandKey MoveRelative(double distance, double velocity)
    {
        if (!double.IsFinite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        return MoveAbsolute(_position + distance, velocity); // Uses CURRENT machine coordinate, not a work offset.
    }
    public void SetWorkOffset(double offset)
    {
        RequireReady();
        if (!double.IsFinite(offset)) throw new ArgumentOutOfRangeException(nameof(offset));
        _workOffset = offset;
    }
    public CommandKey Jog(int direction, double velocity)
    {
        RequireReady();
        if (direction is not (-1 or 1) || !double.IsFinite(velocity) || velocity <= 0 || velocity > _settings.JogVelocityLimit)
            throw new ArgumentOutOfRangeException(nameof(velocity));
        _velocity = direction * velocity; _target = null; _leaseAge = 0;
        return Begin(AxisState.Jogging);
    }
    public void RenewJog(CommandKey key)
    {
        RequireOpen();
        if (_state != AxisState.Jogging || Read().Key != key) throw new InvalidOperationException("Not the active jog.");
        _leaseAge = 0;
    }
    public void RequestStop()
    {
        RequireOpen();
        if (!Read().Busy || _state == AxisState.Stopping) return;
        if (_state == AxisState.Homing) _homed = false;
        _origin = _position; _stopVelocity = _velocity; _elapsed = 0;
        _target = null; _state = AxisState.Stopping;
    }
    private void Fail(string code, bool uncertain = false)
    {
        _fault = code; _state = AxisState.Faulted; _enabled = _homed = false; _target = null; _velocity = 0;
        _result = uncertain ? CommandResult.Uncertain : CommandResult.Faulted; _evidence = CompletionEvidence.None;
    }
    public void SetInputs(AxisInputs inputs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inputs = inputs;
        if (!_connected) return;
        if (!inputs.BusOperational) Fail("BusLost", uncertain: true);
        else if (inputs.Emergency) Fail("EmergencyInput");
        else if (inputs.Alarm) Fail("DriveAlarm");
        else if (inputs.PositiveLimit || inputs.NegativeLimit) Fail("LimitInput");
        else if (_enabled && !inputs.DriveReady) Fail("DriveNotReady");
    }
    public void InjectFollowingOffset(double offset)
    {
        RequireOpen();
        if (!double.IsFinite(offset)) throw new ArgumentOutOfRangeException(nameof(offset));
        _followingOffset = offset; // Test-only fault injection, NOT a hardware API.
    }
    public void ResetFault()
    {
        RequireOpen(); Healthy();
        if (_state != AxisState.Faulted) throw new InvalidOperationException("No fault.");
        if (_settings.HasFeedback && Math.Abs(_followingOffset) > _settings.InPositionTolerance)
            throw new InvalidOperationException("Feedback discrepancy still present.");
        _state = AxisState.Disabled; _enabled = _homed = false; _fault = null;
        // Keep old command RESULT: reset cannot turn it into Completed or replay it.
    }
    private bool CheckPositionAndFeedback()
    {
        if (_position < _settings.Minimum || _position > _settings.Maximum) { Fail("SoftwareTravelBoundary"); return false; }
        if (_settings.HasFeedback && Math.Abs(_followingOffset) > _settings.FollowingErrorLimit) { Fail("FollowingError"); return false; }
        return true;
    }
    private void Complete(bool home = false)
    {
        _state = AxisState.Ready; _velocity = 0; _target = null; _result = CommandResult.Completed;
        _evidence = _settings.HasFeedback ? CompletionEvidence.SimulatedFeedback : CompletionEvidence.CommandOnly;
        if (home) _homed = true;
    }
    public void Tick(double seconds)
    {
        RequireOpen();
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > .1) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!Read().Busy) return;
        _elapsed += seconds;
        switch (_state)
        {
            case AxisState.Homing:
                _position -= _settings.HomeVelocity * seconds;
                if (_inputs.HomeSensorAvailable && _position <= 0)
                {
                    _position = 0;
                    if (CheckPositionAndFeedback())
                    {
                        if (_settings.HasFeedback && (!_inputs.InPosition || Math.Abs(_followingOffset) > _settings.InPositionTolerance))
                            Fail("HomeFeedbackNotSettled");
                        else Complete(home: true);
                    }
                    return;
                }
                if (_elapsed >= _settings.HomeTimeout) { Fail("HomeTimeout"); return; }
                break;
            case AxisState.Moving:
                var sample = _plan!.Sample(_elapsed);
                _position = _origin + _direction * sample.Distance; _velocity = _direction * sample.Velocity;
                if (_elapsed >= _plan.Duration) _state = AxisState.Settling;
                goto case AxisState.Settling;
            case AxisState.Settling:
                if (_elapsed >= _settings.MoveTimeout) { Fail("MoveTimeout"); return; }
                if (_state == AxisState.Settling && CheckPositionAndFeedback() &&
                    (!_settings.HasFeedback || (_inputs.InPosition && Math.Abs(_followingOffset) <= _settings.InPositionTolerance)))
                    Complete();
                break;
            case AxisState.Jogging:
                _position += _velocity * seconds; _leaseAge += seconds;
                if (_leaseAge >= _settings.JogLease) RequestStop();
                break;
            case AxisState.Stopping:
                double duration = Math.Abs(_stopVelocity) / _settings.Deceleration;
                double t = Math.Min(_elapsed, duration);
                double sign = Math.Sign(_stopVelocity);
                _position = _origin + _stopVelocity * t - sign * _settings.Deceleration * t * t / 2;
                _velocity = sign * Math.Max(0, Math.Abs(_stopVelocity) - _settings.Deceleration * t);
                if (_elapsed >= duration)
                {
                    _state = AxisState.Ready; _result = CommandResult.Stopped;
                    _evidence = CompletionEvidence.None;
                }
                break;
        }
        CheckPositionAndFeedback();
    }
    public void Close()
    {
        RequireOpen();
        if (Read().Busy) throw new InvalidOperationException("Observe stop before closing; closing is not a stop command.");
        _connected = _enabled = _homed = false; _state = AxisState.Disconnected;
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (Read().Busy) _result = CommandResult.Uncertain;
        _connected = _enabled = _homed = false; _state = AxisState.Disconnected; _velocity = 0; _target = null; _disposed = true;
        // Disposing this fake object is not evidence that a real axis would have stopped.
    }
}
