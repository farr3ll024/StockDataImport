using Shibusa.Data;
using System.ComponentModel.DataAnnotations.Schema;

namespace Import.Infrastructure.PostgreSQL.DataAccessObjects;

[Table(name: "company_officers", Schema = "public")]
internal class CompanyOfficer
{
    public CompanyOfficer(Guid companyId,
        EodHistoricalData.Sdk.Models.Fundamentals.CommonStock.Officer officer)
    {
        CompanyId = companyId;
        DateCaptured = DateTime.UtcNow;
        Name = officer.Name ?? "Unknown";
        Title = officer.Title;
        YearBorn = officer.YearBorn;
        UtcTimestamp = DateTime.UtcNow;
    }

    public CompanyOfficer(
        Guid companyId,
        DateTime dateCaptured,
        string name,
        string? title,
        string? yearBorn,
        DateTime utcTimestamp)
    {
        CompanyId = companyId;
        DateCaptured = dateCaptured;
        Name = name;
        Title = title;
        YearBorn = yearBorn;
        UtcTimestamp = utcTimestamp;
    }

    [ColumnWithKey("company_id", Order = 1, TypeName = "uuid", IsPartOfKey = true)]
    public Guid CompanyId { get; }

    [ColumnWithKey("date_captured", Order = 2, TypeName = "date", IsPartOfKey = true)]
    public DateTime DateCaptured { get; }

    [ColumnWithKey("name", Order = 3, TypeName = "text", IsPartOfKey = false)]
    public string Name { get; }

    [ColumnWithKey("title", Order = 4, TypeName = "text", IsPartOfKey = false)]
    public string? Title { get; }

    [ColumnWithKey("year_born", Order = 5, TypeName = "text", IsPartOfKey = false)]
    public string? YearBorn { get; }

    [ColumnWithKey("utc_timestamp", Order = 6, TypeName = "timestamp with time zone", IsPartOfKey = false)]
    public DateTime UtcTimestamp { get; }
}
