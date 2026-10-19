using System.Globalization;

string[] rows = ["pump-A,68.5", "pump-B,73.0", "pump-A,72.5", "pump-B,bad"];
List<Reading> readings = [];

for (int index = 0; index < rows.Length; index++)
{
    string[] parts = rows[index].Split(',');
    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
        !double.TryParse(parts[1], NumberStyles.Float,
            CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
    {
        Console.Error.WriteLine($"第 {index + 1} 行无效：{rows[index]}");
        continue;
    }

    readings.Add(new Reading(parts[0], value));
}

var summary = readings
    .GroupBy(item => item.DeviceId, StringComparer.Ordinal)
    .Select(group => new
    {
        DeviceId = group.Key,
        Count = group.Count(),
        Average = group.Average(item => item.Value),
        Alarms = group.Count(item => item.Value >= 70)
    })
    .OrderBy(item => item.DeviceId);

foreach (var row in summary)
    Console.WriteLine($"{row.DeviceId}: {row.Count} 条，均值 {row.Average:F1}，告警 {row.Alarms} 条");

public record Reading(string DeviceId, double Value);
