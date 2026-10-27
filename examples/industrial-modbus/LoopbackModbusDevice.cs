using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace IndustrialModbus;

public enum DeviceFault { None, Busy, Acknowledge, Silent, Close, WrongTransaction, SlowBytes, RejectWrites, ApplyWriteThenDropOnce, AdjustSetpoint, ApplyWriteThenSilence }

// Independent, bounded teaching server, not a complete Modbus implementation.
// Supports FC 01/02/03/04/05/06/0F/10, one client at a time, Unit ID = 1.
public sealed class LoopbackModbusDevice : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;
    private readonly DeviceFault _fault;
    private readonly bool[] _coils = new bool[8];
    private readonly bool[] _inputs = [true, false, true, false, false, false, false, false];
    private readonly ushort[] _holding = [250, 1, 0, 0, 0, 0, 0, 0];
    private readonly ushort[] _registers = [unchecked((ushort)-123), 0x41C8, 0, 0, 0, 0, 0, 0];
    private int _requests;
    private int _writes;
    private bool _droppedWrite;
    private string? _lastError;
    public IPEndPoint Endpoint { get; }
    public int Requests => Volatile.Read(ref _requests);
    public int Writes => Volatile.Read(ref _writes);
    public string? LastError => Volatile.Read(ref _lastError);

    public LoopbackModbusDevice(DeviceFault fault = DeviceFault.None)
    {
        _fault = fault;
        _listener.Start();
        Endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _serve = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                try { await ServeClientAsync(client.GetStream()); }
                catch (Exception error) when (error is IOException or SocketException or FormatException)
                { Volatile.Write(ref _lastError, $"{error.GetType().Name}: {error.Message}"); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _listener.Stop(); }
    }

    private async Task ServeClientAsync(NetworkStream stream)
    {
        using (stream)
        {
            while (true)
            {
                byte[] header = new byte[7];
                int first = await stream.ReadAsync(header.AsMemory(0, 1), _stop.Token);
                if (first == 0) return;
                await stream.ReadExactlyAsync(header.AsMemory(1), _stop.Token);
                int length = U16(header, 4); // Includes Unit ID, which is already in header[6].
                if (U16(header, 2) != 0 || length is < 2 or > 254)
                    throw new FormatException("无效 MBAP 协议号或长度。");
                byte[] pdu = new byte[length - 1];
                await stream.ReadExactlyAsync(pdu, _stop.Token);
                Interlocked.Increment(ref _requests);
                if (_fault == DeviceFault.Close) return;
                if (_fault == DeviceFault.Silent) continue;
                byte[] result = _fault switch
                {
                    DeviceFault.Busy => Error(pdu[0], 6),
                    DeviceFault.Acknowledge => Error(pdu[0], 5),
                    DeviceFault.RejectWrites when pdu[0] is 5 or 6 or 15 or 16 => Error(pdu[0], 3),
                    _ => header[6] == 1 ? Apply(pdu) : Error(pdu[0], 0x0B),
                };
                if (_fault == DeviceFault.ApplyWriteThenSilence && pdu[0] is 5 or 6 or 15 or 16 && result[0] < 0x80) continue;
                if (_fault == DeviceFault.ApplyWriteThenDropOnce && !_droppedWrite
                    && pdu[0] is 5 or 6 or 15 or 16 && result[0] < 0x80)
                {
                    _droppedWrite = true; // Deliberately lose the reply AFTER modifying the simulated value.
                    return;
                }
                byte[] response = new byte[7 + result.Length];
                header.CopyTo(response, 0);
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), (ushort)(result.Length + 1));
                if (_fault == DeviceFault.WrongTransaction)
                    BinaryPrimitives.WriteUInt16BigEndian(response, unchecked((ushort)(U16(header, 0) + 1)));
                result.CopyTo(response, 7);
                if (_fault == DeviceFault.SlowBytes)
                {
                    foreach (byte value in response)
                    {
                        await stream.WriteAsync(new[] { value }, _stop.Token);
                        await Task.Delay(80, _stop.Token);
                    }
                }
                else await stream.WriteAsync(response, _stop.Token);
            }
        }
    }

    private byte[] Apply(byte[] pdu)
    {
        byte function = pdu[0];
        if (function is not (1 or 2 or 3 or 4 or 5 or 6 or 15 or 16)) return Error(function, 1);
        if (pdu.Length < 5) return Error(function, 3);
        int address = U16(pdu, 1), quantity = U16(pdu, 3);
        if (function is 1 or 2 or 3 or 4)
        {
            if (pdu.Length != 5 || quantity < 1 || quantity > (function <= 2 ? 2000 : 125)) return Error(function, 3);
            if (address + quantity > 8) return Error(function, 2);
            if (function <= 2)
            {
                bool[] points = function == 1 ? _coils : _inputs;
                byte[] result = new byte[2 + (quantity + 7) / 8];
                result[0] = function; result[1] = (byte)(result.Length - 2);
                for (int index = 0; index < quantity; index++)
                    if (points[address + index]) result[2 + index / 8] |= (byte)(1 << (index % 8));
                return result;
            }
            ushort[] registers = function == 3 ? _holding : _registers;
            byte[] values = new byte[2 + quantity * 2];
            values[0] = function; values[1] = (byte)(quantity * 2);
            for (int index = 0; index < quantity; index++)
                BinaryPrimitives.WriteUInt16BigEndian(values.AsSpan(2 + index * 2), registers[address + index]);
            return values;
        }
        if (function is 5 or 6)
        {
            if (pdu.Length != 5 || (function == 5 && quantity is not (0 or 0xFF00))) return Error(function, 3);
            if (address >= 8) return Error(function, 2);
            if (function == 5) _coils[address] = quantity == 0xFF00;
            else _holding[address] = (ushort)quantity;
            if (_fault == DeviceFault.AdjustSetpoint && function == 6 && address == 0)
                _holding[0] = (ushort)(quantity == 0 ? 1 : quantity - 1);
            Interlocked.Increment(ref _writes);
            return pdu.ToArray();
        }
        if (quantity < 1 || quantity > (function == 15 ? 1968 : 123) || pdu.Length < 6) return Error(function, 3);
        int byteCount = function == 15 ? (quantity + 7) / 8 : quantity * 2;
        if (pdu[5] != byteCount || pdu.Length != 6 + byteCount) return Error(function, 3);
        if (address + quantity > 8) return Error(function, 2);
        // Validate the whole range and payload before making any changes.
        for (int index = 0; index < quantity; index++)
        {
            if (function == 15) _coils[address + index] = (pdu[6 + index / 8] & (1 << (index % 8))) != 0;
            else _holding[address + index] = U16(pdu, 6 + index * 2);
        }
        Interlocked.Increment(ref _writes);
        return pdu.AsSpan(0, 5).ToArray();
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
    private static byte[] Error(byte function, byte code) => [(byte)(function | 0x80), code];

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _serve; }
        finally { _listener.Stop(); _stop.Dispose(); }
    }
}
