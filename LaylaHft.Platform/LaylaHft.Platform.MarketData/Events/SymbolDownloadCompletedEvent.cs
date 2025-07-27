using FastEndpoints;

namespace LaylaHft.Platform.MarketData.Events;

public class SymbolDownloadCompletedEvent : IEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}