﻿using LaylaHft.Platform.MarketData.Services;

namespace LaylaHft.Platform.MarketData.BackgroundServices;

public class SymbolStatsProcessorService : BackgroundService
{
    private const int WorkerCount = 5;

    private readonly ISymbolStatsQueue _queue;
    private readonly IServiceProvider _sp;
    private readonly ILogger<SymbolStatsProcessorService> _logger;

    public SymbolStatsProcessorService(ISymbolStatsQueue queue, IServiceProvider sp, ILogger<SymbolStatsProcessorService> logger)
    {
        _queue = queue;
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📡 SymbolStatsProcessorService started with {Count} workers.", WorkerCount);

        var tasks = new List<Task>();

        for (int i = 0; i < WorkerCount; i++)
        {
            int workerId = i + 1;
            tasks.Add(Task.Run(() => WorkerLoop(workerId, stoppingToken), stoppingToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task WorkerLoop(int workerId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var evt = await _queue.DequeueAsync(ct);
                using var scope = _sp.CreateScope();

                var store = scope.ServiceProvider.GetRequiredService<ISymbolStore>();
                var calculator = scope.ServiceProvider.GetRequiredService<ISymbolMarketStatsCalculator>();

                var symbol = await store.GetAsync(evt.Exchange, evt.QuoteAsset, evt.Symbol);

                if (symbol == null)
                {
                    _logger.LogWarning("[Worker {Worker}] Symbol not found in store: {Exchange}/{QuoteAsset}/{Symbol}", workerId, evt.Exchange, evt.QuoteAsset, evt.Symbol);
                    continue;
                }

                await calculator.CalculateAsync(symbol);
                await store.Upsert(symbol.Exchange, symbol.QuoteAsset, symbol.Symbol, symbol);

                _logger.LogInformation("✅ [Worker {Worker}] Stats calculated for {Symbol}", workerId, symbol.Symbol);

                await Task.Delay(200, ct); // Throttle processing
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [Worker {Worker}] Error processing market stats", workerId);
            }
        }
    }
}
