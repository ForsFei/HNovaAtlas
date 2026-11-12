var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

Inspection[] inspections =
[
    new("cycle-001", "station-a", new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero), "OK", 220),
    new("cycle-002", "station-a", new DateTimeOffset(2026, 11, 5, 1, 1, 0, TimeSpan.Zero), "NG", 75)
];

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/inspections", (string? station) =>
    Results.Ok(inspections.Where(row => station is null || row.Station == station)));

app.MapGet("/api/inspections/{id}", IResult (string id) =>
{
    var inspection = Array.Find(inspections, row => row.Id == id);
    return inspection is null
        ? Results.Problem(statusCode: 404, title: "Inspection not found")
        : Results.Ok(inspection);
});

app.Run();

record Inspection(string Id, string Station, DateTimeOffset InspectedAt, string Result, int RoiMean);
