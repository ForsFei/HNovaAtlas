using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using NModbus;
using NModbus.Serial;

namespace IndustrialModbus;

public static class ModbusExamples
{
    public static void Configure(IModbusMaster master)
    {
        master.Transport.ReadTimeout = 400;
        master.Transport.WriteTimeout = 400;
        master.Transport.Retries = 0;
        master.Transport.SlaveBusyUsesRetryCount = true;
        master.Transport.WaitToRetryMilliseconds = 50;
        master.Transport.RetryOnOldResponseThreshold = 0;
    }

    // The worker owns the master; cancellation only closes its underlying TCP socket.
    // Always await the worker before returning, even after cancellation/deadline.
    public static async Task<T> ExecuteTcpAsync<T>(IPEndPoint endpoint,
        Func<IModbusMaster, CancellationToken, T> work, TimeSpan timeout, CancellationToken stop = default)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        using var client = new TcpClient(endpoint.AddressFamily);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stop);
        budget.CancelAfter(timeout); // Includes connection + this whole work delegate.
        try
        {
            await client.ConnectAsync(endpoint.Address, endpoint.Port, budget.Token);
            using CancellationTokenRegistration abort = budget.Token.Register(
                static state => ((TcpClient)state!).Dispose(), client);
            T result = await Task.Run(() =>
            {
                budget.Token.ThrowIfCancellationRequested();
                using IModbusMaster master = new ModbusFactory().CreateMaster(client);
                Configure(master);
                return work(master, budget.Token);
            });
            budget.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error) when (budget.IsCancellationRequested)
        {
            stop.ThrowIfCancellationRequested();
            throw new TimeoutException("Modbus TCP 操作期限到达；旧连接已关闭，工作任务已等待结束。", error);
        }
    }

    // Compile-only RTU example; no CLI path invokes this method or opens a COM port.
    public static decimal ReadRtuTemperature(string portName, int baudRate, Parity parity,
        StopBits stopBits, byte unitId)
    {
        if (unitId is 0 or > 247) throw new ArgumentOutOfRangeException(nameof(unitId));
        using var port = new SerialPort(portName, baudRate, parity, 8, stopBits)
        {
            Handshake = Handshake.None, ReadTimeout = 400, WriteTimeout = 400,
        };
        port.Open();
        using IModbusSerialMaster master = new ModbusFactory().CreateRtuMaster(port);
        Configure(master);
        ushort[] raw = master.ReadInputRegisters(unitId, 0, 1);
        return RegisterCodec.SignedTenths(raw[0]);
    }
}
