using FastEndpoints;

namespace LaylaHft.Platform.MarketData.Events;

public class SymbolDownloadedEvent : IEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}