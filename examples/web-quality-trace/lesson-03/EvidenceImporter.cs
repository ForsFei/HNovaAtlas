using IndustrialStationEvidence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

static class EvidenceImporter
{
    public static async Task<(string Id, bool Created)> ImportAsync(
        TraceDb db, string folder, string station)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string[] parts = Path.GetFileName(path).Split('-');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out Guid session)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long cycle)
            || cycle <= 0 || session == Guid.Empty
            || Path.GetFileName(path) != FormattableString.Invariant($"{session:N}-{cycle:D10}"))
        {
            throw new ArgumentException("Pass a published evidence cycle directory to --Import.");
        }

        station = station.Trim();
        if (station.Length is < 1 or > 64 || station.Any(char.IsControl))
            throw new ArgumentException("Station must contain 1 to 64 characters.");

        var evidence = EvidenceStore.ReadOfflineBundle(Path.GetDirectoryName(path)!, session, cycle, out var pgm);
        var manifest = evidence.Manifest;
        string id = FormattableString.Invariant($"{session:N}-{cycle:D10}");
        string result = manifest.Terminal.Judgment!.Value.ToString().ToUpperInvariant();
        double mean = manifest.Inspection.Measurement!.Mean;

        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = await db.Inspections.FindAsync(id);
        if (existing is null)
        {
            db.Inspections.Add(new(id, station, DateTime.UtcNow.Ticks, result, mean,
                manifest.Width, manifest.Height, manifest.ImageSha256, pgm));
            await db.SaveChangesAsync();
        }
        else if (existing.Station != station || existing.Result != result ||
            existing.RoiMean != mean || existing.ImageSha256 != manifest.ImageSha256)
        {
            throw new InvalidDataException("Import conflicts with the existing cycle.");
        }

        await transaction.CommitAsync();
        return (id, existing is null);
    }
}
