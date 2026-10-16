Device[] devices =
[
    new("pump-A", "一号泵"),
    new("pump-B", "二号泵"),
    new("pump-C", "备用泵")
];
Reading[] readings =
[
    new("PUMP-A", 70),
    new("pump-A", 71),
    new("pump-B", 68),
    new("missing", 99)
];
StringComparer keys = StringComparer.OrdinalIgnoreCase;

var inner = devices.Join(readings, device => device.Id, reading => reading.DeviceId,
    (device, reading) => (device.Id, reading.Value), keys).ToArray();
Check(inner.Select(row => row.Value).SequenceEqual([70, 71, 68]),
    "Join excludes unmatched keys and keeps both duplicate matches");

var grouped = devices.GroupJoin(readings, device => device.Id, reading => reading.DeviceId,
    (device, matches) => new { device.Id, Values = matches.Select(x => x.Value).ToArray() },
    keys).ToArray();
Check(grouped.Length == 3 && grouped[0].Values.SequenceEqual([70, 71])
    && grouped[2].Values.Length == 0, "GroupJoin retains the unmatched outer device");

var left = devices.LeftJoin(readings, device => device.Id, reading => reading.DeviceId,
    (device, reading) => (device.Id, Value: (int?)reading?.Value), keys).ToArray();
Check(left.Length == 4 && left.Select(row => row.Value).SequenceEqual([70, 71, 68, null]),
    "LeftJoin flattens matches and uses null for the missing right item");

var portableLeft = devices.GroupJoin(readings, device => device.Id,
        reading => reading.DeviceId, (device, matches) => new { device, matches }, keys)
    .SelectMany(group => group.matches.DefaultIfEmpty(),
        (group, reading) => (group.device.Id, Value: (int?)reading?.Value))
    .ToArray();
Check(portableLeft.SequenceEqual(left), "GroupJoin plus DefaultIfEmpty gives the same left rows");

var exact = devices.Join(readings, device => device.Id, reading => reading.DeviceId,
    (device, reading) => reading.Value, StringComparer.Ordinal).ToArray();
Check(exact.SequenceEqual([71, 68]), "an ordinal comparer does not match PUMP-A to pump-A");

Console.WriteLine("LINQ 连接、空匹配和键比较断言全部通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public record Device(string Id, string Name);
public record Reading(string DeviceId, int Value);
