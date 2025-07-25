using FastEndpoints;
using LaylaHft.Platform.MarketData.Events;
using LaylaHft.Platform.MarketData.Services;

namespace LaylaHft.Platform.MarketData.Handlers;

public class SymbolMarketStatsHandler : IEventHandler<SymbolImportedEvent>
{
    private readonly ISymbolStore _store;
    private readonly ISymbolStatsQueue _queue;
    private readonly ILogger<SymbolMarketStatsHandler> _logger;

    public SymbolMarketStatsHandler(
        ISymbolStore store,
        ISymbolStatsQueue queue,
        ILogger<SymbolMarketStatsHandler> logger)
    {
        _store = store;
        _queue = queue;
        _logger = logger;
    }

    public async Task HandleAsync(SymbolImportedEvent e, CancellationToken ct)
    {
        try
        {
            await _queue.EnqueueAsync(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while enqueing even symbol {Symbol}", e.Symbol);
        }
    }
}