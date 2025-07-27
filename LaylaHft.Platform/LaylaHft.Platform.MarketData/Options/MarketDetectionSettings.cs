namespace LaylaHft.Platform.MarketData.Options;

public class MarketDetectionSettings
{
    public decimal VolumeSpikeRatioThreshold { get; set; }
    public decimal MinVolume { get; set; }
    public decimal PriceJumpRatioThreshold { get; set; }
    public decimal VolatilitySpikeRatioThreshold { get; set; }
}