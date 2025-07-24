namespace LaylaHft.Platform.Domains;

using MessagePack;

[MessagePackObject]
public class SymbolMetadata
{
    [Key(0)] public string Symbol { get; set; }
    [Key(1)] public string Name { get; set; }
    [Key(2)] public string Exchange { get; set; }
    [Key(3)] public string BaseAsset { get; set; }
    [Key(4)] public string QuoteAsset { get; set; }
    [Key(5)] public int PricePrecision { get; set; }
    [Key(6)] public int QuantityPrecision { get; set; }
    [Key(7)] public decimal TickSize { get; set; }
    [Key(8)] public decimal StepSize { get; set; }
    [Key(9)] public decimal MinQty { get; set; }
    [Key(10)] public decimal MaxQty { get; set; }
    [Key(11)] public decimal MinPrice { get; set; }
    [Key(12)] public decimal MaxPrice { get; set; }
    [Key(13)] public decimal MinNotional { get; set; }
    [Key(14)] public decimal CurrentPrice { get; set; }
    [Key(15)] public decimal Change24hPct { get; set; }
    [Key(16)] public decimal Change7dPct { get; set; }
    [Key(17)] public decimal Change30dPct { get; set; }
    [Key(18)] public SparklineData Sparkline { get; set; } = new();
    [Key(19)] public string IconUrl { get; set; }
    [Key(20)] public SymbolStatus Status { get; set; }
}

[MessagePackObject]
public class SparklineData
{
    [Key(0)] public List<decimal> Data { get; set; } = new();
    [Key(1)] public string Color { get; set; }
}

public enum SymbolStatus
{
    Active,
    Inactive,
    Suspended,
    Delisted
}