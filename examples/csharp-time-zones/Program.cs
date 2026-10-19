using System.Globalization;

var captured = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(8));
var utc = captured.ToUniversalTime();
Check(utc == new DateTimeOffset(2024, 12, 31, 16, 0, 0, TimeSpan.Zero), "UTC instant");
Check(captured == utc && captured.Offset != utc.Offset, "same instant, different offsets");

string wire = captured.ToString("O", CultureInfo.InvariantCulture);
var restored = DateTimeOffset.ParseExact(wire, "O", CultureInfo.InvariantCulture, DateTimeStyles.None);
Check(restored.EqualsExact(captured), "round-trip keeps wall time and offset");

var starts = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
    new DateTime(1, 1, 1, 2, 0, 0), 3, 9);
var ends = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
    new DateTime(1, 1, 1, 2, 0, 0), 11, 2);
var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
    new DateTime(2025, 1, 1), new DateTime(2025, 12, 31),
    TimeSpan.FromHours(1), starts, ends);
var zone = TimeZoneInfo.CreateCustomTimeZone(
    "Training/Eastern-2025", TimeSpan.FromHours(-5),
    "Training zone", "Standard", "Daylight", [rule]);

var missing = new DateTime(2025, 3, 9, 2, 30, 0, DateTimeKind.Unspecified);
var repeated = new DateTime(2025, 11, 2, 1, 30, 0, DateTimeKind.Unspecified);
Check(zone.IsInvalidTime(missing), "spring gap");
Check(zone.IsAmbiguousTime(repeated), "fall overlap");

TimeSpan[] offsets = zone.GetAmbiguousTimeOffsets(repeated);
Check(offsets.Contains(TimeSpan.FromHours(-4)) && offsets.Contains(TimeSpan.FromHours(-5)),
    "two possible offsets");
var firstOccurrence = new DateTimeOffset(repeated, TimeSpan.FromHours(-4));
var secondOccurrence = new DateTimeOffset(repeated, TimeSpan.FromHours(-5));
Check(secondOccurrence - firstOccurrence == TimeSpan.FromHours(1), "two distinct instants");

var beforeJump = new DateTimeOffset(2025, 3, 9, 1, 30, 0, TimeSpan.FromHours(-5));
var afterElapsedHour = TimeZoneInfo.ConvertTime(beforeJump.AddHours(1), zone);
Check(afterElapsedHour.Hour == 3 && afterElapsedHour.Offset == TimeSpan.FromHours(-4),
    "elapsed hour crosses the gap");

Console.WriteLine("时间点、往返格式与夏令时边界断言全部通过");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"断言失败：{name}");
}
