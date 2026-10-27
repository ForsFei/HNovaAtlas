using IndustrialS7;

using var stop = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    if (args is ["--offline-check"]) { SelfChecks.Offline(); return 0; }
    if (args is ["--self-test"]) { SelfChecks.Offline(); await SelfChecks.NetworkAsync(stop.Token); return 0; }
    if (args.Length != 0 && args is not ["--demo"])
    {
        Console.Error.WriteLine("用法：--offline-check | --self-test | --demo。只使用本机内置服务，不接受现场地址。");
        return 2;
    }
    await S7Examples.RunDemoAsync(stop.Token);
    Console.WriteLine("Plc 与 Snap7 本机服务均已释放；写入仅修改测试 DB，不证明现场动作完成。");
    return 0;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { Console.WriteLine("已取消并等待退出。"); return 0; }
catch (Exception error) { Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}"); return 1; }
finally { Console.CancelKeyPress -= cancel; }
