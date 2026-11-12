using System.Globalization;

static class TimeRange
{
    public static bool TryReadUtc(string? input, out long? ticks)
    {
        ticks = null;
        if (string.IsNullOrEmpty(input)) return true;
        if (!DateTimeOffset.TryParseExact(input, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var time))
            return false;
        ticks = time.UtcTicks;
        return true;
    }
}
