using System.Globalization;
using System.Net;
using System.Net.Sockets;
using IndustrialNetworking;

if (args is ["--self-test"])
{
    await SelfChecks.RunAsync();
    return 0;
}

if (args.Length is < 2 or > 3 || !int.TryParse(args[1], out int port) || port is < 1024 or > 65535
    || args[0] is not ("--tcp-device" or "--tcp-client" or "--udp-device" or "--udp-client"))
{
    Console.WriteLine("用法：--self-test | --tcp-device 43120 [normal|split|slow|close|partial|malformed|wrongid]");
    Console.WriteLine("      --tcp-client 43120 | --udp-device 43121 [normal|slow|drop|malformed|wrongid|stalethenvalid|wrongsourcethenvalid|empty|oversize] | --udp-client 43121");
    Console.WriteLine("所有 CLI 端点仅使用 127.0.0.1，不扫描或访问局域网设备。");
    return args.Length == 0 ? 0 : 2;
}

DeviceScenario scenario = DeviceScenario.Normal;
if (args.Length == 3 && (!args[0].EndsWith("-device", StringComparison.Ordinal)
    || !Enum.TryParse(args[2], true, out scenario) || !Enum.IsDefined(scenario))) return 2;
DeviceScenario[] tcpScenarios = [DeviceScenario.Normal, DeviceScenario.Split, DeviceScenario.Slow,
    DeviceScenario.Close, DeviceScenario.Partial, DeviceScenario.Malformed, DeviceScenario.WrongId];
if (args[0] == "--tcp-device" && !tcpScenarios.Contains(scenario)) return 2;
if (args[0] == "--udp-device" && scenario is DeviceScenario.Split or DeviceScenario.Close or DeviceScenario.Partial) return 2;

using var stop = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    if (args[0].EndsWith("-client", StringComparison.Ordinal))
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        decimal value = args[0] == "--tcp-client"
            ? await NetworkQueries.ReadTcpAsync(endpoint, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), stop.Token)
            : await NetworkQueries.ReadUdpAsync(endpoint, TimeSpan.FromSeconds(1), stop.Token, Console.WriteLine);
        Console.WriteLine($"有效响应：{value.ToString("0.0", CultureInfo.InvariantCulture)} °C；本次资源已释放。");
    }
    else
    {
        void Ready(IPEndPoint endpoint) => Console.WriteLine($"教学设备 {endpoint}, {scenario}；Ctrl+C 停止。");
        if (args[0] == "--tcp-device") await DeviceSimulator.RunTcpAsync(port, scenario, Ready, stop.Token, Console.WriteLine);
        else await DeviceSimulator.RunUdpAsync(port, scenario, Ready, stop.Token, Console.WriteLine);
    }
    return 0;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { Console.WriteLine("操作已取消。"); return 0; }
catch (Exception error) when (error is IOException or SocketException or FormatException or TimeoutException)
{
    Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}");
    return 1;
}
finally { Console.CancelKeyPress -= cancel; }
