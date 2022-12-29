using System.ComponentModel.DataAnnotations.Schema;
using Shibusa.Data;

namespace Import.Infrastructure.PostgreSQL.DataAccessObjects;

[Table(name: "etf_asset_allocations", Schema = "public")]
internal class EtfAssetAllocation
{
    public EtfAssetAllocation(Guid etfId,
        string category,
        EodHistoricalData.Sdk.Models.Fundamentals.Etf.AssetAllocationItem assetAllocation)
    {
        EtfId = etfId;
        DateCaptured = DateTime.UtcNow;
        Category = category;
        LongPercentage = Convert.ToDouble(assetAllocation.LongPercentage);
        ShortPercentage = Convert.ToDouble(assetAllocation.ShortPercentage);
        NetAssetsPercentage = Convert.ToDouble(assetAllocation.NetAssetsPercentage);
        UtcTimestamp = DateTime.UtcNow;
    }

    public EtfAssetAllocation(
        Guid etfId,
        DateTime dateCaptured,
        string category,
        double longPercentage,
        double shortPercentage,
        double netAssetsPercentage,
        DateTime utcTimestamp)
    {
        EtfId = etfId;
        DateCaptured = dateCaptured;
        Category = category;
        LongPercentage = longPercentage;
        ShortPercentage = shortPercentage;
        NetAssetsPercentage = netAssetsPercentage;
        UtcTimestamp = utcTimestamp;
    }


    [ColumnWithKey("etf_id", Order = 1, TypeName = "uuid", IsPartOfKey = true)]
    public Guid EtfId { get;  }

    [ColumnWithKey("date_captured", Order = 2, TypeName = "date", IsPartOfKey = true)]
    public DateTime DateCaptured { get;  }

    [ColumnWithKey("category", Order = 3, TypeName = "text", IsPartOfKey = false)]
    public string Category { get;  }

    [ColumnWithKey("long_percentage", Order = 4, TypeName = "double precision", IsPartOfKey = false)]
    public double LongPercentage { get;  }

    [ColumnWithKey("short_percentage", Order = 5, TypeName = "double precision", IsPartOfKey = false)]
    public double ShortPercentage { get;  }

    [ColumnWithKey("net_assets_percentage", Order = 6, TypeName = "double precision", IsPartOfKey = false)]
    public double NetAssetsPercentage { get;  }

    [ColumnWithKey("utc_timestamp", Order = 7, TypeName = "timestamp with time zone", IsPartOfKey = false)]
    public DateTime UtcTimestamp { get;  }
}
