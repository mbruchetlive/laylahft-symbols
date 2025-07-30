using MessagePack;

namespace LaylaHft.Platform.Domains;

[MessagePackObject]
public class CandleSnapshot()
{
    [Key(0)] public DateTime OpenTime { get; set; }
    [Key(1)] public DateTime CloseTime { get; set; }
    [Key(2)] public decimal Open { get; set; }
    [Key(3)] public decimal High { get; set; }
    [Key(4)] public decimal Low { get; set; }
    [Key(5)] public decimal Close { get; set; }
    [Key(6)] public decimal Volume { get; set; }
    [Key(7)] public string Symbol { get; set; } = string.Empty;
    [Key(8)] public string Interval { get; set; } = string.Empty;
    [Key(9)] public double Weight { get; set; }

    public CandleSnapshot(DateTime openTime, DateTime closeTime, decimal open, decimal high, decimal low, decimal close, decimal volume, string symbol, string interval):this()
    {
        OpenTime = openTime;
        CloseTime = closeTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Symbol = symbol;
        Interval = interval;
    }

}