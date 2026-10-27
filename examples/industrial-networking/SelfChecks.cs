using System.Net;
using System.Net.Sockets;

namespace IndustrialNetworking;

public static class SelfChecks
{
    public static async Task RunAsync()
    {
        int checks = 0;
        string id = WireProtocol.NewId();
        Check(WireProtocol.ParseRequest(WireProtocol.DecodeDatagram(WireProtocol.Request(id))) == id, "请求往返");
        TemperatureReply reply = WireProtocol.ParseResponse(WireProtocol.DecodeDatagram(WireProtocol.Response(id)));
        Check(reply.RequestId == id && reply.Temperature == 25.3m, "响应往返");
        foreach (int raw in new[] { -400, -15, 0, 1250 })
            Check(WireProtocol.ParseResponse($"TEMP {id} {raw}").Temperature == raw / 10m, "符号与量程边界");
        foreach (string bad in new[] { $"TEMP {id} 1251", $"TEMP {id} -401", $"TEMP {id} 25.3",
            $"TEMP  {id} 253", $"TEMP {id} abc", "TEMP bad 253" })
            await Throws<FormatException>(() => Task.FromResult(WireProtocol.ParseResponse(bad)), "非法响应");
        foreach (byte[] bad in new byte[][] { [], "TEMP"u8.ToArray(), [0xFF, 0x0D, 0x0A],
            [.. WireProtocol.Response(id), .. WireProtocol.Response(id)], new byte[512] })
            await Throws<FormatException>(() => Task.FromResult(WireProtocol.DecodeDatagram(bad)), "数据报边界");

        await WithDevice(false, DeviceScenario.Normal, async (endpoint, token) =>
        {
            Check(await Tcp(endpoint, token) == 25.3m, "TCP 正常请求");
            Check(await Tcp(endpoint, token) == 25.3m, "显式新连接再次查询，使用新请求号");
        });
        await WithDevice(false, DeviceScenario.Split, async (e, t) => Check(await Tcp(e, t) == 25.3m, "TCP 分次写入响应"));
        foreach (DeviceScenario fault in new[] { DeviceScenario.Close, DeviceScenario.Partial })
            await WithDevice(false, fault, (e, t) => Throws<EndOfStreamException>(() => Tcp(e, t), $"TCP {fault}"));
        foreach (DeviceScenario fault in new[] { DeviceScenario.Malformed, DeviceScenario.WrongId })
            await WithDevice(false, fault, (e, t) => Throws<FormatException>(() => Tcp(e, t), $"TCP {fault}"));
        await WithDevice(false, DeviceScenario.Slow, (e, t) => Throws<TimeoutException>(
            () => NetworkQueries.ReadTcpAsync(e, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(150), t), "TCP 响应超时"));
        await WithDevice(false, DeviceScenario.Slow, async (e, t) =>
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(t);
            cancel.CancelAfter(100);
            await Throws<OperationCanceledException>(() => Tcp(e, cancel.Token), "TCP 用户取消不是超时");
        });

        await WithDevice(true, DeviceScenario.Normal, async (e, t) => Check(await Udp(e, t) == 25.3m, "UDP 正常请求"));
        foreach (DeviceScenario scenario in new[] { DeviceScenario.StaleThenValid, DeviceScenario.WrongSourceThenValid })
            await WithDevice(true, scenario, async (e, t) =>
            {
                int ignored = 0;
                decimal value = await NetworkQueries.ReadUdpAsync(e, TimeSpan.FromSeconds(1), t, _ => ignored++);
                Check(value == 25.3m && ignored == 1, $"UDP 过滤 {scenario} 后接收有效结果");
            });
        foreach (DeviceScenario fault in new[] { DeviceScenario.Drop, DeviceScenario.WrongId, DeviceScenario.Slow })
            await WithDevice(true, fault, (e, t) => Throws<TimeoutException>(
                () => NetworkQueries.ReadUdpAsync(e, TimeSpan.FromMilliseconds(150), t), $"UDP {fault} 最终期限"));
        foreach (DeviceScenario fault in new[] { DeviceScenario.Malformed, DeviceScenario.Empty, DeviceScenario.Oversize })
            await WithDevice(true, fault, (e, t) => Throws<FormatException>(() => Udp(e, t), $"UDP {fault}"));
        await WithDevice(true, DeviceScenario.Drop, async (e, t) =>
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(t);
            cancel.CancelAfter(100);
            await Throws<OperationCanceledException>(() => Udp(e, cancel.Token), "UDP 用户取消不是超时");
        });

        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var closedEndpoint = (IPEndPoint)closed.LocalEndpoint;
        closed.Stop();
        try
        {
            await Tcp(closedEndpoint, CancellationToken.None);
            throw new InvalidOperationException("未监听端口不应连接成功。");
        }
        catch (Exception error) when (error is SocketException or TimeoutException)
        {
            // Windows may retry SYN before surfacing refusal; our deadline can win.
            checks++;
        }
        Console.WriteLine($"PASS: {checks} checks; real loopback TCP/UDP sockets, no LAN/hardware access.");

        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++;
        }

        async Task Throws<T>(Func<Task> action, string name) where T : Exception
        {
            try { await action(); }
            catch (T) { checks++; return; }
            throw new InvalidOperationException($"{name}: 期待 {typeof(T).Name}");
        }
    }

    private static Task<decimal> Tcp(IPEndPoint endpoint, CancellationToken token) =>
        NetworkQueries.ReadTcpAsync(endpoint, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), token);
    private static Task<decimal> Udp(IPEndPoint endpoint, CancellationToken token) =>
        NetworkQueries.ReadUdpAsync(endpoint, TimeSpan.FromSeconds(1), token);

    private static async Task WithDevice(bool udp, DeviceScenario scenario,
        Func<IPEndPoint, CancellationToken, Task> check)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var ready = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task server = udp
            ? DeviceSimulator.RunUdpAsync(0, scenario, e => ready.SetResult(e), stop.Token)
            : DeviceSimulator.RunTcpAsync(0, scenario, e => ready.SetResult(e), stop.Token);
        try { await check(await ready.Task.WaitAsync(stop.Token), stop.Token); }
        finally
        {
            stop.Cancel();
            await server; // Observe server completion and release sockets; no orphaned tasks.
        }
    }
}
