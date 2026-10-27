using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using IndustrialModbus;
using NModbus;

namespace IndustrialModbusMonitor;

public enum MonitorState { Stopped, Polling, Retrying, Faulted }
public enum WriteState { Queued, Sending, Verified, Mismatch, Rejected, NotSent, Uncertain, Acknowledged }
public sealed record WriteReceipt(Guid Id, ushort Expected, WriteState State, ushort? ReadBack, string Message);
public sealed record DeviceSample(decimal Temperature, float FloatValue, decimal Target, ushort Mode,
    ushort Count, bool Ready, string RawHex, long Timestamp)
{
    public TimeSpan Age => Stopwatch.GetElapsedTime(Timestamp);
}
public sealed record ModbusSnapshot(MonitorState State, DeviceSample? Sample, WriteReceipt? Write,
    bool WriteBusy, int ReadFailures, string Message);

public sealed class ModbusMonitor
{
    private sealed record Command(Guid Id, ushort Raw, long QueuedAt);
    private readonly object _gate = new();
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _budget;
    private readonly TimeSpan _queueLifetime;
    private Command? _pending;
    private CancellationToken _stop;
    private bool _running;
    private int _started;
    private ModbusSnapshot _snapshot = new(MonitorState.Stopped, null, null, false, 0, "未启动。");
    public ModbusSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public ModbusMonitor(TimeSpan? pollInterval = null, TimeSpan? budget = null, TimeSpan? queueLifetime = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _budget = budget ?? TimeSpan.FromSeconds(1.5);
        _queueLifetime = queueLifetime ?? TimeSpan.FromSeconds(2);
        foreach (TimeSpan value in new[] { _pollInterval, _budget, _queueLifetime })
            if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public bool TryQueueTarget(string text, out string error)
    {
        if (!ModbusValueCodec.TrySetpoint(text, out ushort raw, out error)) return false;
        lock (_gate)
        {
            if (!_running || _stop.IsCancellationRequested || _snapshot.State != MonitorState.Polling
                || _snapshot.Sample is not DeviceSample sample || sample.Age > TimeSpan.FromSeconds(3) || _snapshot.WriteBusy)
            {
                error = "需要新鲜的有效采样，且没有其他排队/在途写入。";
                return false;
            }
            _pending = new(Guid.NewGuid(), raw, Stopwatch.GetTimestamp());
            Volatile.Write(ref _snapshot, _snapshot with
            {
                WriteBusy = true,
                Write = new(_pending.Id, raw, WriteState.Queued, null, "已排队，尚未发送。"),
            });
            return true;
        }
    }

    public async Task RunAsync(IPEndPoint endpoint, CancellationToken stop)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("每次启动使用新监控对象。");
        lock (_gate) { _running = true; _stop = stop; }
        Change(s => s with { State = MonitorState.Polling, Message = "等待首次采样。" });
        try
        {
            while (!stop.IsCancellationRequested)
            {
                Command? command;
                lock (_gate) { command = _pending; _pending = null; }
                bool started = false, acknowledged = false, outcomeKnown = false;
                try
                {
                    DeviceSample sample = await ModbusExamples.ExecuteTcpAsync(endpoint, (master, token) =>
                    {
                        if (command is not null)
                        {
                            token.ThrowIfCancellationRequested();
                            ModbusSnapshot beforeSend = Snapshot;
                            if (Stopwatch.GetElapsedTime(command.QueuedAt) > _queueLifetime
                                || beforeSend.State != MonitorState.Polling || beforeSend.Sample is not DeviceSample fresh
                                || fresh.Age > TimeSpan.FromSeconds(3))
                            {
                                Receipt(command, WriteState.NotSent, "排队或采样已过期，没有发送。" );
                                outcomeKnown = true;
                            }
                            else
                            {
                                Receipt(command, WriteState.Sending, "准备发送；停止操作不等于撤回。" );
                                started = true;
                                try
                                {
                                    master.WriteSingleRegister(1, 0, command.Raw);
                                    acknowledged = true;
                                    ushort readBack = master.ReadHoldingRegisters(1, 0, 1)[0];
                                    Receipt(command, readBack == command.Raw ? WriteState.Verified : WriteState.Mismatch,
                                        readBack == command.Raw ? "写入回执正常且读回一致；不证明动作完成。" : "写入回执正常，但读回与期望不一致。", readBack);
                                    outcomeKnown = true;
                                }
                                catch (SlaveException error)
                                {
                                    Receipt(command, acknowledged ? WriteState.Acknowledged : WriteState.Rejected,
                                        acknowledged ? $"已收到写回执，但读回异常 {error.SlaveExceptionCode:X2}。"
                                            : $"设备明确返回写入异常 {error.SlaveExceptionCode:X2}。" );
                                    outcomeKnown = true;
                                }
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        ushort[] input = master.ReadInputRegisters(1, 0, 3);
                        ushort[] holding = master.ReadHoldingRegisters(1, 0, 3);
                        bool ready = master.ReadInputs(1, 0, 1)[0];
                        return new DeviceSample(RegisterCodec.SignedTenths(input[0]),
                            ModbusValueCodec.Float32(input[1], input[2], RegisterOrder.ABCD), holding[0] / 10m,
                            holding[1], holding[2], ready, string.Join(" ", input.Select(v => v.ToString("X4"))), Stopwatch.GetTimestamp());
                    }, _budget, stop);
                    Change(s => s with { State = MonitorState.Polling, Sample = sample, ReadFailures = 0,
                        Message = "采样有效；各功能区为顺序读取，不承诺设备原子快照。" });
                }
                catch (Exception error) when (error is IOException or SocketException or TimeoutException
                    or SlaveException or OperationCanceledException or FormatException or ArgumentException or InvalidOperationException)
                {
                    if (command is not null && !outcomeKnown)
                        Receipt(command, acknowledged ? WriteState.Acknowledged : started ? WriteState.Uncertain : WriteState.NotSent,
                            acknowledged ? "已收到写回执，但未完成读回；不自动重发。"
                                : started ? "写入已尝试，最终结果未知；不自动重发。" : "尚未尝试写入，不自动重排。" );
                    if (stop.IsCancellationRequested) break;
                    bool protocolFailure = error is SlaveException or FormatException or ArgumentException or InvalidOperationException;
                    lock (_gate)
                    {
                        InvalidateQueued();
                        Change(s => s with { State = protocolFailure || s.ReadFailures + 1 >= 3 ? MonitorState.Faulted : MonitorState.Retrying,
                            Sample = null, ReadFailures = s.ReadFailures + 1, Message = $"{error.GetType().Name}: {error.Message}" });
                    }
                    if (Snapshot.State == MonitorState.Faulted) return;
                }
                finally
                {
                    if (command is not null) Change(s => s.Write?.Id == command.Id ? s with { WriteBusy = false } : s);
                }
                await Task.Delay(_pollInterval, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            lock (_gate) { _running = false; }
            InvalidateQueued();
            Change(s => s with { State = stop.IsCancellationRequested ? MonitorState.Stopped : MonitorState.Faulted,
                Sample = null, WriteBusy = false, Message = stop.IsCancellationRequested ? "已停止；旧值失效，写入结果见记录。" : s.Message });
        }
    }

    private void Receipt(Command command, WriteState state, string message, ushort? readBack = null) =>
        Change(s => s with { Write = new(command.Id, command.Raw, state, readBack, message) });

    private void InvalidateQueued()
    {
        lock (_gate)
        {
            if (_pending is not Command command) return;
            _pending = null;
            Volatile.Write(ref _snapshot, _snapshot with { WriteBusy = false,
                Write = new(command.Id, command.Raw, WriteState.NotSent, null, "停止或读取失败，取消尚未发送的排队写入。") });
        }
    }

    private void Change(Func<ModbusSnapshot, ModbusSnapshot> change)
    {
        lock (_gate) Volatile.Write(ref _snapshot, change(_snapshot));
    }
}
