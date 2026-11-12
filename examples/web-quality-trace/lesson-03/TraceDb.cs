using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

sealed class TraceDb(DbContextOptions<TraceDb> options) : DbContext(options)
{
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<InspectionReview> Reviews => Set<InspectionReview>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Inspection>().HasIndex(row => new { row.ImportedTicks, row.Id });
        builder.Entity<InspectionReview>().HasKey(row => row.InspectionId);
        builder.Entity<InspectionReview>().HasOne<Inspection>().WithOne()
            .HasForeignKey<InspectionReview>(row => row.InspectionId);
    }

    public static readonly Expression<Func<Inspection, InspectionSummary>> Summary = row =>
        new(row.Id, row.Station, new DateTime(row.ImportedTicks, DateTimeKind.Utc), row.Result, row.RoiMean);
}

sealed record Inspection(string Id, string Station, long ImportedTicks, string Result, double RoiMean,
    int Width, int Height, string ImageSha256, byte[] Pgm);

sealed record InspectionPage(IReadOnlyList<InspectionSummary> Items, int Total, int Page, int PageSize);

sealed record InspectionSummary(string Id, string Station, DateTime ImportedAt, string Result, double RoiMean);

sealed record InspectionReview(string InspectionId, string Decision, string Note, string Reviewer, DateTimeOffset ReviewedAt);
