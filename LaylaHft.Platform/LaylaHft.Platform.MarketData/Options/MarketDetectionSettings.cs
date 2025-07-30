public class MarketDetectionSettings
{
    public Dictionary<string, MarketDetectionThresholds> TimeframeThresholds { get; set; }
        = new();

    public MarketDetectionThresholds GetThresholdsForTF(string tf)
    {
        if (string.IsNullOrWhiteSpace(tf))
            return GetDefaultThresholds();

        tf = tf.ToUpperInvariant();

        // Mapping automatique
        string normalizedTF = tf switch
        {
            "M1" or "M3" or "M5" or "M15" => "M1",
            "H1" or "H2" or "H4" or "H6" or "H8" or "H12" => "H1",
            "D1" or "D3" or "W1" or "MN" => "D1",
            _ => tf
        };

        if (TimeframeThresholds.TryGetValue(normalizedTF, out var thresholds))
            return thresholds;

        // Fallback codé en dur
        return GetDefaultThresholds();
    }

    private MarketDetectionThresholds GetDefaultThresholds()
    {
        return new MarketDetectionThresholds
        {
            VolumeSpikeRatioThreshold = 1.2m,
            MinVolume = 1000,
            PriceJumpRatioThreshold = 1.01m,
            VolatilitySpikeRatioThreshold = 1.1m
        };
    }
}

public class MarketDetectionThresholds
{
    public decimal VolumeSpikeRatioThreshold { get; set; }
    public decimal MinVolume { get; set; }
    public decimal PriceJumpRatioThreshold { get; set; }
    public decimal VolatilitySpikeRatioThreshold { get; set; }
}
