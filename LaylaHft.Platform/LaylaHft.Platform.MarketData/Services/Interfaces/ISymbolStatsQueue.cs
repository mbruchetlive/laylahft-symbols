using LaylaHft.Platform.Domains;

namespace LaylaHft.Platform.MarketData.Services.Interfaces;

public interface ISymbolStatsQueue
{
    Task EnqueueAsync(SymbolMetadata e);
    ValueTask<SymbolMetadata> DequeueAsync(CancellationToken ct);
    Task WaitForProcessingAsync(CancellationToken cancellationToken);
    void MarkProcessed();
    void Reset();
}