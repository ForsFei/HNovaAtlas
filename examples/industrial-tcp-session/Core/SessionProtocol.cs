using IndustrialNetworking;

namespace IndustrialTcpSession;

public enum RequestKind { Ping, Temperature }
public sealed record SessionReply(RequestKind Kind, string Id, decimal? Temperature = null);

public static class SessionProtocol
{
    public static byte[] Request(RequestKind kind, string id) => kind == RequestKind.Ping
        ? WireProtocol.Encode($"PING {id}") : WireProtocol.Request(id);
    public static byte[] Response(RequestKind kind, string id) => kind == RequestKind.Ping
        ? WireProtocol.Encode($"PONG {id}") : WireProtocol.Response(id);

    public static (RequestKind Kind, string Id) ParseRequest(string frame)
    {
        if (frame.StartsWith("PING ", StringComparison.Ordinal))
            return (RequestKind.Ping, ValidateId(frame[5..]));
        return (RequestKind.Temperature, WireProtocol.ParseRequest(frame));
    }

    public static SessionReply ParseResponse(string frame)
    {
        if (frame.StartsWith("PONG ", StringComparison.Ordinal))
            return new(RequestKind.Ping, ValidateId(frame[5..]));
        TemperatureReply reply = WireProtocol.ParseResponse(frame);
        return new(RequestKind.Temperature, reply.RequestId, reply.Temperature);
    }

    private static string ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out Guid value) || value.ToString("N") != id)
            throw new FormatException("无效请求编号。");
        return id;
    }
}
