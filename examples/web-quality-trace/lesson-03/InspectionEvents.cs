using System.Globalization;
using System.ComponentModel;
using System.Net;
using Microsoft.EntityFrameworkCore;

static class InspectionEvents
{
    public static async Task InitializeAsync(TraceDb db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS InspectionEventStream (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                StreamId TEXT NOT NULL
            );
            INSERT INTO InspectionEventStream (Id, StreamId)
                VALUES (1, lower(hex(randomblob(16)))) ON CONFLICT (Id) DO NOTHING;

            CREATE TABLE IF NOT EXISTS InspectionEvents (
                Sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                InspectionId TEXT NOT NULL UNIQUE REFERENCES Inspections(Id)
            );

            CREATE TRIGGER IF NOT EXISTS AppendInspectionEvent
            AFTER INSERT ON Inspections
            BEGIN
                INSERT INTO InspectionEvents (InspectionId) VALUES (NEW.Id);
            END;

            INSERT INTO InspectionEvents (InspectionId)
                SELECT i.Id FROM Inspections i
                WHERE NOT EXISTS (
                    SELECT 1 FROM InspectionEvents e WHERE e.InspectionId = i.Id
                )
                ORDER BY i.ImportedTicks, i.Id;
            """);
        await transaction.CommitAsync();
    }

    public static void MapInspectionEvents(this WebApplication app)
    {
        app.MapGet("/api/inspection-events", async Task<IResult> (
            string? after, int? limit, HttpContext http, TraceDb db, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
                return Results.StatusCode(403);

            int size = limit ?? 20;
            if (size is < 1 or > 100 ||
                !long.TryParse(after ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out long cursor))
                return Results.Problem(statusCode: 400, title: "Invalid after or limit");

            var streams = await db.Database.SqlQueryRaw<StreamHead>("""
                SELECT StreamId, COALESCE((SELECT MAX(Sequence) FROM InspectionEvents), 0) AS Head
                FROM InspectionEventStream WHERE Id = 1
                """).ToListAsync(ct);
            var stream = streams.Single();
            if (cursor > stream.Head)
                return Results.Problem(statusCode: 409, title: "Cursor is ahead of the stream");
            var rows = await db.Database.SqlQuery<EventRow>($"""
                SELECT e.Sequence, i.Id, i.Station, i.Result, i.ImportedTicks
                FROM InspectionEvents e JOIN Inspections i ON i.Id = e.InspectionId
                WHERE e.Sequence > {cursor} AND e.Sequence <= {stream.Head}
                ORDER BY e.Sequence
                LIMIT {size + 1}
                """).ToListAsync(ct);

            var items = rows.Take(size).Select(row => new InspectionEventItem(
                Format(row.Sequence), row.Id, row.Station, row.Result,
                new DateTime(row.ImportedTicks, DateTimeKind.Utc))).ToArray();

            return Results.Ok(new InspectionEventPage(stream.StreamId, items,
                items.Length == 0 ? Format(cursor) : items[^1].Sequence, rows.Count > size));
        }).WithName("ReadInspectionEvents").Produces<InspectionEventPage>()
          .ProducesProblem(400).ProducesProblem(403).ProducesProblem(409).ProducesProblem(500);
    }

    private static string Format(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);

    private sealed record StreamHead(string StreamId, long Head);
    private sealed record EventRow(long Sequence, string Id, string Station, string Result, long ImportedTicks);
}

sealed record InspectionEventPage(
    [property: Description("Identity of this producer database. Store it with the consumer cursor.")] string StreamId,
    IReadOnlyList<InspectionEventItem> Items,
    [property: Description("Last delivered sequence, or the input cursor on an empty page.")] string NextAfter,
    [property: Description("Whether more events existed in the captured stream head.")] bool HasMore);

sealed record InspectionEventItem(
    [property: Description("Positive signed-64-bit sequence encoded as a decimal string. Gaps are allowed.")] string Sequence,
    string InspectionId, string Station, string Result, DateTime ImportedAt);
