using FastEndpoints;
using LaylaHft.Platform.MarketData.BackgroundServices;
using LaylaHft.Platform.MarketData.Events;

namespace LaylaHft.Platform.MarketData.Handlers;

public class SymbolDownloadCompletedHandler : IEventHandler<SymbolDownloadedEvent>
{
    private readonly MarketDataCollectorBackgroundService _collector;

    public SymbolDownloadCompletedHandler(MarketDataCollectorBackgroundService collector)
    {
        _collector = collector;
    }

    public Task HandleAsync(SymbolDownloadedEvent _, CancellationToken ct)
    {
        _collector.NotifyStart(ct); // débloque les TCS
        return Task.CompletedTask;
    }
}