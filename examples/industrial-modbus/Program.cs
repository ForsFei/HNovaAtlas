using IndustrialModbus;

if (args is ["--self-test"])
{
    await SelfChecks.RunAsync();
    return 0;
}
if (args.Length != 0 && args is not ["--demo"])
{
    Console.Error.WriteLine("用法：--demo | --self-test。仅操作本机内置设备，无真实串口命令。");
    return 2;
}

using var stop = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    await using var device = new LoopbackModbusDevice();
    Console.WriteLine($"本机 Modbus TCP 教学设备：{device.Endpoint}，Unit ID=1；写入仅修改模拟值。");
    string result = await ModbusExamples.ExecuteTcpAsync(device.Endpoint, (master, token) =>
    {
        const byte unit = 1;
        ushort[] input = master.ReadInputRegisters(unit, 0, 3);
        token.ThrowIfCancellationRequested();
        master.WriteSingleCoil(unit, 0, true);
        master.WriteMultipleCoils(unit, 2, [true, false, true]);
        master.WriteSingleRegister(unit, 0, 300);
        master.WriteMultipleRegisters(unit, 1, [2, 7]);
        token.ThrowIfCancellationRequested();
        bool enabled = master.ReadCoils(unit, 0, 1)[0];
        bool ready = master.ReadInputs(unit, 0, 1)[0];
        ushort[] holding = master.ReadHoldingRegisters(unit, 0, 3);
        return $"温度={RegisterCodec.SignedTenths(input[0]):0.0} °C，" +
            $"浮点示例={RegisterCodec.FloatHighWordFirst(input[1], input[2]):0.0}，" +
            $"模拟 Enable={enabled}，Ready={ready}，目标原始值={holding[0]}，Mode={holding[1]}，计数={holding[2]}。";
    }, TimeSpan.FromSeconds(3), stop.Token);
    Console.WriteLine(result);
    Console.WriteLine("客户端已释放；退出时停止并等待模拟器。读回一致不证明现场动作完成。");
    return 0;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { Console.WriteLine("已取消并完成清理。"); return 0; }
catch (Exception error) { Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}"); return 1; }
finally { Console.CancelKeyPress -= cancel; }
