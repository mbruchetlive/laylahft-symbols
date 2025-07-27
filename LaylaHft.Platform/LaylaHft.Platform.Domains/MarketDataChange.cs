

namespace LaylaHft.Platform.Domains;

public class MarketDataChange
{
    public string Symbol { get; set; }
    public string Timeframe { get; set; }
    public DateTime Timestamp { get; set; }
    public List<string> ChangeTypes { get; set; }
    public CandleSnapshot Candle { get; set; }
    public string Context { get; set; }
    public string Source { get; set; }
}
