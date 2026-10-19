using System.Text.Json;
using System.Text.Json.Serialization;

DeviceMessage sent = new TemperatureMessage { DeviceId = "pump-A", Celsius = 72.5m };
string json = JsonSerializer.Serialize<DeviceMessage>(sent);
using (JsonDocument document = JsonDocument.Parse(json))
    Check(document.RootElement.GetProperty("$kind").GetString() == "temperature",
        "wire format has an explicit discriminator");

DeviceMessage received = ReadAndValidate(json);
Check(received is TemperatureMessage { DeviceId: "pump-A", Celsius: 72.5m },
    "known kind round-trips to the declared subtype");

DeviceMessage heartbeat = ReadAndValidate(
    """{"$kind":"heartbeat","DeviceId":"pump-B","Sequence":3}""");
Check(heartbeat is HeartbeatMessage { Sequence: 3 }, "second kind is selected");

Expect<JsonException>(() => ReadAndValidate(
    """{"$kind":"remote.type.Name","DeviceId":"pump-A"}"""));
Expect<JsonException>(() => ReadAndValidate(
    """{"$kind":"temperature","DeviceId":"pump-A"}"""));
Expect<InvalidDataException>(() => ReadAndValidate(
    """{"$kind":"temperature","DeviceId":" ","Celsius":72.5}"""));
Console.WriteLine("JSON 类型允许列表、契约错误与业务校验断言全部通过。");

static DeviceMessage ReadAndValidate(string json)
{
    DeviceMessage message = JsonSerializer.Deserialize<DeviceMessage>(json)
        ?? throw new InvalidDataException("消息不能为 null");
    return message switch
    {
        TemperatureMessage t when !string.IsNullOrWhiteSpace(t.DeviceId)
            && t.Celsius is >= -100m and <= 200m => t,
        HeartbeatMessage h when !string.IsNullOrWhiteSpace(h.DeviceId)
            && h.Sequence >= 0 => h,
        _ => throw new InvalidDataException("消息业务值无效")
    };
}

static void Expect<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"预期 {typeof(TException).Name}");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(TemperatureMessage), "temperature")]
[JsonDerivedType(typeof(HeartbeatMessage), "heartbeat")]
public abstract record DeviceMessage;

public sealed record TemperatureMessage : DeviceMessage
{
    public required string DeviceId { get; init; }
    public required decimal Celsius { get; init; }
}

public sealed record HeartbeatMessage : DeviceMessage
{
    public required string DeviceId { get; init; }
    public required int Sequence { get; init; }
}
