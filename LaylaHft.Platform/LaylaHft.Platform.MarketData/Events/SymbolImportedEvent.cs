using FastEndpoints;

namespace LaylaHft.Platform.MarketData.Events;

public class SymbolImportedEvent : IEvent
{
    public string Symbol { get; set; } = default!;
    public string Exchange { get; set; } = default!;
    public string QuoteAsset { get; set; } = default!;
}