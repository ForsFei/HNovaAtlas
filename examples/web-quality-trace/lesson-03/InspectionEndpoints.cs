using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

static class InspectionEndpoints
{
    public static void MapInspectionEndpoints(this WebApplication app, bool reviewEnabled, bool readLimitEnabled)
    {
        var inspectionList = app.MapGet("/api/inspections", async Task<IResult> (
            string? station, string? result, int? page, int? pageSize, bool? newest, string? since, string? until, string? format, string? reviewStatus, HttpContext http, TraceDb db, CancellationToken ct) =>
        {
            bool csv = format == "csv";
            if (format is not null && !csv)
                return Results.Problem(statusCode: 400, title: "Unsupported export format");
            int number = page ?? 1;
            int size = pageSize ?? 20;
            if (number < 1 || size is < 1 or > 100 || (long)(number - 1) * size > int.MaxValue)
                return Results.Problem(statusCode: 400, title: "Invalid page or pageSize");

            if (!TimeRange.TryReadUtc(since, out var start) || !TimeRange.TryReadUtc(until, out var end)
                || (start.HasValue && end.HasValue && start >= end))
                return Results.Problem(statusCode: 400, title: "Use UTC timestamps and an increasing time range");

            reviewStatus = string.IsNullOrEmpty(reviewStatus) ? null : reviewStatus;
            if (reviewStatus is not null && (!reviewEnabled || reviewStatus is not ("pending" or "reviewed")))
                return Results.Problem(statusCode: 400, title: "Enable review and use pending or reviewed");

            IQueryable<Inspection> query = db.Inspections.AsNoTracking();
            if (reviewStatus is not null)
            {
                bool reviewed = reviewStatus == "reviewed";
                query = query.Where(row => row.Result == "NG"
                    && db.Reviews.Any(review => review.InspectionId == row.Id) == reviewed);
            }
            if (!string.IsNullOrEmpty(station)) query = query.Where(row => row.Station == station);
            if (!string.IsNullOrEmpty(result)) query = query.Where(row => row.Result == result);
            if (start.HasValue) query = query.Where(row => row.ImportedTicks >= start.Value);
            if (end.HasValue) query = query.Where(row => row.ImportedTicks < end.Value);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            int total = await query.CountAsync(ct);
            var ordered = newest == true
                ? query.OrderByDescending(row => row.ImportedTicks).ThenByDescending(row => row.Id)
                : query.OrderBy(row => row.ImportedTicks).ThenBy(row => row.Id);
            if (csv)
            {
                if (total > 1000)
                    return Results.Problem(statusCode: 422, title: "Narrow the filters to at most 1000 records");
                var exported = await ordered.Select(TraceDb.Summary).ToListAsync(ct);
                await transaction.CommitAsync(ct);
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(InspectionCsv.Encode(exported), "text/csv; charset=utf-8", "inspections.csv");
            }
            var rows = await ordered
                .Skip((number - 1) * size).Take(size).Select(TraceDb.Summary).ToListAsync(ct);

            await transaction.CommitAsync(ct);
            return Results.Ok(new InspectionPage(rows, total, number, size));
        }).WithName("ListInspections")
          .Produces<InspectionPage>().ProducesProblem(400).ProducesProblem(422).ProducesProblem(429).ProducesProblem(500);
        if (readLimitEnabled) inspectionList.RequireRateLimiting(ReadRateLimit.Policy);

        app.MapGet("/api/inspections/{id}", async Task<IResult> (string id, TraceDb db, CancellationToken ct) =>
        {
            var row = await db.Inspections.Where(row => row.Id == id).Select(TraceDb.Summary).SingleOrDefaultAsync(ct);
            return row is null ? Results.Problem(statusCode: 404, title: "Inspection not found") : Results.Ok(row);
        }).WithName("GetInspection").Produces<InspectionSummary>()
          .ProducesProblem(404).ProducesProblem(500);

        app.MapGet("/api/inspections/{id}/image", async Task<IResult> (string id, HttpContext http, TraceDb db, CancellationToken ct) =>
        {
            var image = await db.Inspections.Where(row => row.Id == id)
                .Select(row => new { row.Width, row.Height, row.Pgm }).SingleOrDefaultAsync(ct);
            if (image is null) return Results.Problem(statusCode: 404, title: "Inspection not found");

            byte[] bmp = Preview.ToBmp(image.Pgm, image.Width, image.Height);
            var tag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(bmp))}\"");
            http.Response.Headers.CacheControl = "private, no-cache";
            return Results.File(bmp, "image/bmp", entityTag: tag);
        }).WithName("GetInspectionImage").Produces(200, contentType: "image/bmp").Produces(304)
          .ProducesProblem(404).ProducesProblem(412).ProducesProblem(500);

        app.MapGet("/api/inspections/{id}/evidence", async Task<IResult> (string id, HttpContext http, TraceDb db, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var evidence = await db.Inspections.Where(row => row.Id == id)
                .Select(row => new { row.Pgm, row.ImageSha256 }).SingleOrDefaultAsync(ct);
            if (evidence is null) return Results.Problem(statusCode: 404, title: "Inspection not found");

            byte[] digest = SHA256.HashData(evidence.Pgm);
            if (!string.Equals(Convert.ToHexString(digest), evidence.ImageSha256, StringComparison.OrdinalIgnoreCase))
                return Results.Problem(statusCode: 500, title: "Evidence integrity check failed");

            http.Response.Headers["Content-Digest"] = $"sha-256=:{Convert.ToBase64String(digest)}:";
            return Results.File(evidence.Pgm, "application/octet-stream", fileDownloadName: $"{id}.pgm");
        }).WithName("DownloadInspectionEvidence").Produces(200, contentType: "application/octet-stream")
          .ProducesProblem(404).ProducesProblem(500);
    }
}
