namespace IndustrialHandshake;

// A deterministic teaching controller, NOT a PLC safety program or a physical actuator.
internal sealed class Mailbox
{
    private readonly object _gate = new();
    private readonly ushort[] _staged = new ushort[6];
    private uint _boot = 1;
    private ushort _highWater, _accepted, _completed, _control, _controlAck, _replyId, _executions, _revision;
    private Ticket _current;
    private ActionState _state;
    private ResultCode _result;
    private Reply _reply;
    private bool _interlock = true, _holdProgress, _failDuringRun;
    public bool Interlock { get { lock (_gate) return _interlock; } set { lock (_gate) _interlock = value; } }
    public bool HoldProgress { get { lock (_gate) return _holdProgress; } set { lock (_gate) _holdProgress = value; } }
    public bool FailDuringRun { get { lock (_gate) return _failDuringRun; } set { lock (_gate) _failDuringRun = value; } }

    public Snapshot Read()
    {
        lock (_gate) return new(_revision, _boot, _highWater, _current, _state, _result, _accepted, _completed,
            _control, _controlAck, _replyId, _reply, _executions);
    }

    private void Changed()
    {
        // Refuse revision rollover: this short-lived demo does not solve ABA across wraparound.
        _revision = checked((ushort)(_revision + 2));
    }

    private void EnsureCanPublish()
    {
        if (_revision > ushort.MaxValue - 2) throw new InvalidOperationException("Demo publication revision exhausted; no rollover allowed.");
    }

    public void Stage(int offset, ushort[] words)
    {
        lock (_gate)
        {
            if (_control != 0 || _controlAck != 0) throw new InvalidOperationException("Mailbox control is not reset.");
            if (offset < 0 || words.Length == 0 || offset + words.Length > 6) throw new ArgumentOutOfRangeException(nameof(offset));
            words.CopyTo(_staged, offset); // Frozen candidate is latched only by non-zero control.
        }
    }

    public void SetControl(ushort control)
    {
        lock (_gate)
        {
            if (control > 2) throw new ArgumentOutOfRangeException(nameof(control));
            EnsureCanPublish();
            if (control == 0)
            {
                _control = _controlAck = 0;
                Changed();
                return;
            }
            if (_controlAck != 0)
            {
                if (_control == control) return; // Held level is NOT a new command.
                throw new InvalidOperationException("Reset control before another operation.");
            }
            _control = control;
            Ticket candidate = Ticket.Decode(_staged);
            _replyId = candidate.RequestId;
            _reply = candidate.BootId != _boot ? Reply.EpochMismatch : candidate.RequestId == 0 ? Reply.Invalid :
                control == 1 ? Submit(candidate) : Release(candidate);
            _controlAck = control;
            Changed();
        }
    }

    private Reply Submit(Ticket candidate)
    {
        if (_state != ActionState.Idle && candidate.RequestId == _current.RequestId)
            return candidate == _current ? Reply.Duplicate : Reply.Conflict;
        if (candidate.RequestId <= _highWater) return Reply.Stale;
        if (_state != ActionState.Idle) return Reply.Busy; // Does not overwrite current or consume a new identity.
        _current = candidate;
        _highWater = candidate.RequestId;
        _accepted = _completed = 0;
        _result = candidate.Version != 1 || candidate.Operation != 1 ? ResultCode.Unsupported :
            candidate.Target is < 0 or > 1000 ? ResultCode.Range : !Interlock ? ResultCode.Interlock : ResultCode.None;
        _state = _result == ResultCode.None ? ActionState.Accepted : ActionState.Rejected;
        if (_state == ActionState.Accepted) _accepted = candidate.RequestId;
        return Reply.Recorded; // Mailbox recorded it; State distinguishes business acceptance/rejection.
    }

    private Reply Release(Ticket candidate)
    {
        if (_state == ActionState.Idle || candidate.RequestId != _current.RequestId) return Reply.Stale;
        if (candidate != _current) return Reply.Conflict;
        if (_state is not (ActionState.Done or ActionState.Rejected or ActionState.Faulted)) return Reply.NotTerminal;
        _current = default;
        _accepted = _completed = 0;
        _state = ActionState.Idle;
        _result = ResultCode.None;
        return Reply.Released; // High-water mark survives release within this boot.
    }

    public void Advance()
    {
        lock (_gate)
        {
            if (HoldProgress) return;
            if (_state is ActionState.Accepted or ActionState.Running) EnsureCanPublish();
            if (_state == ActionState.Accepted)
            {
                if (!Interlock) { _state = ActionState.Faulted; _result = ResultCode.Interlock; }
                else { _executions = checked((ushort)(_executions + 1)); _state = ActionState.Running; }
                Changed();
            }
            else if (_state == ActionState.Running)
            {
                _state = FailDuringRun || !Interlock ? ActionState.Faulted : ActionState.Done;
                _result = _state == ActionState.Faulted ? ResultCode.ExecutionFault : ResultCode.None;
                if (_state == ActionState.Done) _completed = _current.RequestId;
                Changed();
            }
        }
    }

    public void Reboot()
    {
        lock (_gate)
        {
            _boot = checked(_boot + 1);
            _highWater = _accepted = _completed = _control = _controlAck = _replyId = _revision = 0;
            _current = default; _state = ActionState.Idle; _result = ResultCode.None; _reply = Reply.None;
            Array.Clear(_staged);
            // ExecutionCount is retained solely as a test diagnostic, NOT a durable request journal.
        }
    }
}
