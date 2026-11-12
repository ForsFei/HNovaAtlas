using System.Globalization;
using System.Text;

static class InspectionCsv
{
    public static byte[] Encode(IEnumerable<InspectionSummary> rows)
    {
        var csv = new StringBuilder("Id,Station,ImportedAtUtc,Result,RoiMean\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(',', Cell(row.Id), Cell(row.Station),
                Cell(row.ImportedAt.ToString("O", CultureInfo.InvariantCulture)), Cell(row.Result),
                Cell(row.RoiMean.ToString("R", CultureInfo.InvariantCulture)));
            csv.Append("\r\n");
        }
        return [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Cell(string value)
    {
        // Spreadsheet-facing text export. Prefix formula-like values before CSV escaping.
        var first = value.TrimStart();
        if (first.Length > 0 && "=+-@\t\r\n".Contains(first[0]) ||
            value.Length > 0 && "\t\r\n".Contains(value[0]))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
