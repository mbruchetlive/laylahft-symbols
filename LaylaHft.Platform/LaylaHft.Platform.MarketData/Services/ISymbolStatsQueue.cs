using LaylaHft.Platform.MarketData.Events;

namespace LaylaHft.Platform.MarketData.Services;

public interface ISymbolStatsQueue
{
    Task EnqueueAsync(SymbolImportedEvent e);
    ValueTask<SymbolImportedEvent> DequeueAsync(CancellationToken ct);
}