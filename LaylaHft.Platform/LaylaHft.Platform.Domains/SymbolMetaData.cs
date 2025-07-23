namespace LaylaHft.Platform.Domains;

public class SymbolMetadata
{
    public string Symbol { get; set; }
    public string Name { get; set; }
    public string Exchange { get; set; }

    public string BaseAsset { get; set; }
    public string QuoteAsset { get; set; }

    public int PricePrecision { get; set; }
    public int QuantityPrecision { get; set; }

    public decimal TickSize { get; set; }
    public decimal StepSize { get; set; }

    public decimal MinQty { get; set; }
    public decimal MaxQty { get; set; }

    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }

    public decimal MinNotional { get; set; }

    public decimal CurrentPrice { get; set; }

    public decimal Change24hPct { get; set; }
    public decimal Change7dPct { get; set; }
    public decimal Change30dPct { get; set; }

    public SparklineData Sparkline { get; set; } = new();

    public string IconUrl { get; set; }

    public SymbolStatus Status { get; set; } = SymbolStatus.Active;
}

public class SparklineData
{
    public List<decimal> Data { get; set; } = new();
    public string Color { get; set; }
}

public enum SymbolStatus
{
    Active,
    Inactive,
    Suspended,
    Delisted
}