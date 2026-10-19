using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
foreach (string id in new[] { "pump-A", "PUMP-A", "pump-B" })
{
    counts.TryGetValue(id, out int previous);
    counts[id] = previous + 1;
}
Require(counts["pump-a"] == 2, "集合比较器");

List<int> values = [1, 2, 3];
IEnumerable<int> query = values.Where(x => x > 1);
int[] snapshot = query.ToArray();
values.Add(4);
Require(query.SequenceEqual([2, 3, 4]), "延迟查询");
Require(snapshot.SequenceEqual([2, 3]), "查询快照");

string json = """{"deviceId":"pump-A","value":72.5}""";
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
};
ReadingDto? dto = JsonSerializer.Deserialize<ReadingDto>(json, jsonOptions);
Require(dto is { DeviceId: "pump-A", Value: 72.5 }, "JSON 契约");

string xml = """
<devices xmlns="urn:hnova:readings">
  <reading id="pump-A">72.5</reading>
  <reading id="pump-B">68.0</reading>
</devices>
""";
var xmlSettings = new XmlReaderSettings
{
    DtdProcessing = DtdProcessing.Prohibit,
    XmlResolver = null,
    MaxCharactersInDocument = 100_000
};
using var input = new StringReader(xml);
using var reader = XmlReader.Create(input, xmlSettings);
XDocument document = XDocument.Load(reader);
XNamespace ns = "urn:hnova:readings";
int validReadings = 0;
foreach (XElement element in document.Root?.Elements(ns + "reading") ?? [])
{
    string? id = (string?)element.Attribute("id");
    bool valid = !string.IsNullOrWhiteSpace(id)
        && double.TryParse(element.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double value)
        && double.IsFinite(value);
    Require(valid, "XML 读数");
    validReadings++;
}
Require(validReadings == 2, "XML 读数数量");

Console.WriteLine("数据处理示例验证通过：集合、LINQ、JSON、XML");

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"验证失败：{label}");
}

public sealed class ReadingDto
{
    [JsonPropertyName("deviceId")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("value")]
    public required double Value { get; init; }
}
