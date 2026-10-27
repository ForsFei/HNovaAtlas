namespace IndustrialHandshake;

internal enum ActionState : ushort { Idle, Accepted, Running, Done, Rejected, Faulted }
internal enum Reply : ushort { None, Recorded, Duplicate, Conflict, Busy, Stale, EpochMismatch, Invalid, Released, NotTerminal }
internal enum ResultCode : ushort { None, Interlock, Range, Unsupported, ExecutionFault }
internal enum Resolution { Known, NotSubmitted, Unknown, EpochChanged, NotRetained, Conflict, WaitTimeout, WaitCanceled }

internal readonly record struct Ticket(uint BootId, ushort RequestId, ushort Operation, short Target, ushort Version = 1)
{
    public ushort[] Encode() => [(ushort)BootId, (ushort)(BootId >> 16), RequestId, Operation, unchecked((ushort)Target), Version];
    public static Ticket Decode(ReadOnlySpan<ushort> words)
    {
        if (words.Length != 6) throw new InvalidDataException("Expected six request words.");
        return new((uint)words[0] | (uint)words[1] << 16, words[2], words[3], unchecked((short)words[4]), words[5]);
    }
}

internal sealed record Snapshot(ushort Revision, uint BootId, ushort HighWater, Ticket Current,
    ActionState State, ResultCode Result, ushort AcceptedId, ushort CompletedId,
    ushort Control, ushort ControlAck, ushort ReplyId, Reply Reply, ushort ExecutionCount)
{
    public bool Terminal => State is ActionState.Done or ActionState.Rejected or ActionState.Faulted;
    public ushort[] Encode() => [Revision, (ushort)BootId, (ushort)(BootId >> 16), HighWater, Current.RequestId,
        (ushort)State, (ushort)Result, AcceptedId, CompletedId, unchecked((ushort)Current.Target), Current.Operation,
        Current.Version, Control, ControlAck, ReplyId, (ushort)Reply, ExecutionCount, Revision];

    public static Snapshot Decode(ushort[] words)
    {
        if (words.Length != 18 || words[0] != words[17] || words[0] % 2 != 0)
            throw new InvalidDataException("Incomplete or inconsistent status publication.");
        uint boot = (uint)words[1] | (uint)words[2] << 16;
        var state = (ActionState)words[5];
        var reply = (Reply)words[15];
        var result = (ResultCode)words[6];
        if (boot == 0 || !Enum.IsDefined(state) || !Enum.IsDefined(reply) || !Enum.IsDefined(result) ||
            words[12] > 2 || words[13] > 2 ||
            (state == ActionState.Idle ? words[4] != 0 || words[7] != 0 || words[8] != 0 : words[4] == 0) ||
            (state is ActionState.Accepted or ActionState.Running or ActionState.Done or ActionState.Faulted && words[7] != words[4]) ||
            (state == ActionState.Rejected && words[7] != 0) || words[4] > words[3] ||
            (state is ActionState.Idle or ActionState.Accepted or ActionState.Running or ActionState.Done ?
                result != ResultCode.None : result == ResultCode.None) ||
            (state == ActionState.Done ? words[8] != words[4] : words[8] != 0))
            throw new InvalidDataException("Invalid status identity/state combination.");
        Ticket current = state == ActionState.Idle ? default : new Ticket(boot, words[4], words[10], unchecked((short)words[9]), words[11]);
        return new(words[0], boot, words[3], current,
            state, result, words[7], words[8], words[12], words[13], words[14], reply, words[16]);
    }
}

internal sealed record Observation(Resolution Resolution, Snapshot? Snapshot, string Detail);
