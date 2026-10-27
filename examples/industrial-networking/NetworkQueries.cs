using System.Net;
using System.Net.Sockets;
using SerialFraming;

namespace IndustrialNetworking;

public static class NetworkQueries
{
    // One request per TCP connection in this introductory example.
    public static async Task<decimal> ReadTcpAsync(IPEndPoint device, TimeSpan connectTimeout,
        TimeSpan responseTimeout, CancellationToken stop = default)
    {
        ValidateTimeout(connectTimeout);
        ValidateTimeout(responseTimeout);
        using var client = new TcpClient(device.AddressFamily) { NoDelay = true };
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            connect.CancelAfter(connectTimeout);
            try { await client.ConnectAsync(device.Address, device.Port, connect.Token); }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested && connect.IsCancellationRequested)
            { throw new TimeoutException("TCP 建立连接超时。"); }
        }

        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(stop);
        exchange.CancelAfter(responseTimeout);
        try
        {
            using NetworkStream stream = client.GetStream();
            string requestId = WireProtocol.NewId();
            await stream.WriteAsync(WireProtocol.Request(requestId), exchange.Token);
            string frame = await ReadFrameAsync(stream, exchange.Token);
            TemperatureReply reply = WireProtocol.ParseResponse(frame);
            if (reply.RequestId != requestId) throw new FormatException("TCP 响应请求号不匹配。");
            return reply.Temperature;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested && exchange.IsCancellationRequested)
        { throw new TimeoutException("TCP 请求响应超时；连接已放弃，不自动重发。"); }
    }

    public static async Task<decimal> ReadUdpAsync(IPEndPoint device, TimeSpan responseTimeout,
        CancellationToken stop = default, Action<string>? ignored = null)
    {
        ValidateTimeout(responseTimeout);
        // Explicit loopback binding matches the local simulator scope of this demo.
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(stop);
        exchange.CancelAfter(responseTimeout);
        try
        {
            string requestId = WireProtocol.NewId();
            byte[] request = WireProtocol.Request(requestId);
            await udp.SendAsync(request, device, exchange.Token);
            while (true)
            {
                UdpReceiveResult received = await udp.ReceiveAsync(exchange.Token);
                if (!received.RemoteEndPoint.Equals(device))
                {
                    ignored?.Invoke("忽略来源端点不匹配的数据报。");
                    continue;
                }
                TemperatureReply reply = WireProtocol.ParseResponse(WireProtocol.DecodeDatagram(received.Buffer));
                if (reply.RequestId != requestId)
                {
                    ignored?.Invoke("忽略旧请求或其他请求的数据报。");
                    continue;
                }
                return reply.Temperature;
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested && exchange.IsCancellationRequested)
        { throw new TimeoutException("UDP 请求响应超时；不自动重发。"); }
    }

    public static async Task<string> ReadFrameAsync(NetworkStream stream, CancellationToken stop)
    {
        var decoder = new CrlfFrameDecoder(WireProtocol.MaxPayload);
        byte[] buffer = new byte[256];
        while (true)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(), stop);
            if (count == 0)
                throw new EndOfStreamException(decoder.BufferedBytes == 0
                    ? "对端在完整响应前关闭。" : "对端关闭，仍残留半帧。");
            IReadOnlyList<string> frames = decoder.Feed(buffer.AsSpan(0, count));
            if (frames.Count == 0) continue;
            if (frames.Count != 1 || decoder.BufferedBytes != 0)
                throw new FormatException("单次请求收到多余帧或残留字节。");
            return frames[0];
        }
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout), "期限必须是有限的正数，且在 CancelAfter 支持范围内。");
    }
}
