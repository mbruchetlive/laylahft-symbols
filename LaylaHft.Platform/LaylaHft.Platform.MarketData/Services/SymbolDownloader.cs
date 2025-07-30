using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Events;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LaylaHft.Platform.MarketData.Services;

public class SymbolDownloader
{
    private readonly IBinanceRestClient _client;
    private readonly ISymbolStore _store;
    private readonly ILogger<SymbolDownloader> _logger;
    private readonly ISymbolStatsQueue _queue;
    private volatile bool _isLoading = false;
    public bool IsLoading => _isLoading;
    private Timer? _reconnectTimer;
    private bool _isInFallback;

    public bool IsOnline { get; private set; }
    public DateTime LastDownloadDate { get; private set; }

    public SymbolDownloader(
        IBinanceRestClient client,
        ISymbolStore store,
        ISymbolStatsQueue queue,
        ILogger<SymbolDownloader> logger)
    {
        _client = client;
        _store = store;
        _logger = logger;
        _queue = queue;
    }

    public void StartConnectivityMonitor(CancellationToken cancellationToken)
    {
        if (_reconnectTimer != null) return; // Évite les doublons

        _reconnectTimer = new Timer(async _ =>
        {
            if (_isLoading) return;

            try
            {
                var result = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync();
                if (result.Success && result.Data != null)
                {
                    _logger.LogInformation("[Symbols] Binance is back online. Exiting FAILBACK MODE. Reloading symbols...");
                    _reconnectTimer?.Dispose();
                    _reconnectTimer = null;
                    _isInFallback = false;

                    await LoadInitialSymbolsAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Symbols] Failed to load from Binance. Entering FAILBACK MODE.");
                _isInFallback = true;
            }
        },
        null,
        TimeSpan.FromSeconds(10),    // Première tentative rapide
        TimeSpan.FromMinutes(1));    // Ensuite toutes les minutes
    }

    public async Task<int> LoadInitialSymbolsAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("[Symbols] Starting initial symbol download from Binance...");

            // 1 Récupération ExchangeInfo
            var exchangeInfoResult = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync();
            
            if (!exchangeInfoResult.Success || exchangeInfoResult.Data == null)
                throw new Exception("Failed to retrieve ExchangeInfo");

            _logger.LogInformation(message:"[{Exchange}] ExchangeInfo loaded successfully. Found {Count} symbols.", "Binance", exchangeInfoResult.Data.Symbols.Length);

            var tradableSymbols = exchangeInfoResult.Data.Symbols
                .Where(s => s.Status == Binance.Net.Enums.SymbolStatus.Trading && s.IsSpotTradingAllowed)
                .Select(s => s.Name)
                .ToList();

            // 2 Récupération tickers par batch de 100 symboles (max 5 en parallèle)
            var allTickers = new ConcurrentBag<IBinanceTick>();
            var semaphore = new SemaphoreSlim(5); // max 5 appels simultanés

            var chunkTasks = tradableSymbols
                .Chunk(100)
                .Select(async chunk =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var tickersResult = await _client.SpotApi.ExchangeData.GetTickersAsync(chunk.ToList(), cancellationToken);
                        if (tickersResult.Success && tickersResult.Data != null)
                        {
                            foreach (var ticker in tickersResult.Data)
                                allTickers.Add(ticker);
                        }
                        else
                        {
                            _logger.LogWarning("[Symbols] Failed to get tickers for chunk. Error: {Error}", tickersResult.Error);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

            await Task.WhenAll(chunkTasks);

            // 3 Enrichissement symboles avec tickers
            var enrichedSymbols = exchangeInfoResult.Data.Symbols
                .Join(allTickers, s => s.Name, t => t.Symbol, (s, t) => new
                {
                    Symbol = s,
                    Ticker = t
                })
                .ToList();

            // 4 Sélection top 100 volume / prix / change
            var topVolume = enrichedSymbols.OrderByDescending(x => x.Ticker.Volume).Take(100);
            var topPrice = enrichedSymbols.OrderByDescending(x => x.Ticker.LastPrice).Take(100);
            var topChange = enrichedSymbols.OrderByDescending(x => Math.Abs(x.Ticker.PriceChangePercent)).Take(100);

            var selected = topVolume
                .Concat(topPrice)
                .Concat(topChange)
                .DistinctBy(x => x.Symbol.Name)
                .ToList();

            // 5 Stockage + mise en queue symbole par symbole
            foreach (var entry in selected)
            {
                var meta = new SymbolMetadata
                {
                    Symbol = entry.Symbol.Name,
                    Name = $"{entry.Symbol.BaseAsset} / {entry.Symbol.QuoteAsset}",
                    Exchange = "Binance",
                    BaseAsset = entry.Symbol.BaseAsset,
                    QuoteAsset = entry.Symbol.QuoteAsset,
                    CurrentPrice = entry.Ticker.LastPrice,
                    Change24hPct = entry.Ticker.PriceChangePercent,
                    Volume = entry.Ticker.Volume,
                    IconUrl = $"https://cdn.exemple.com/assets/icons/{entry.Symbol.BaseAsset}.svg"
                };

                await _store.Upsert(meta.Exchange, meta.QuoteAsset, meta.Symbol, meta);

                // Mise en queue pour stats
                await _queue.EnqueueAsync(meta);
            }

            await _store.SaveToFileAsync();
            stopwatch.Stop();

            _logger.LogInformation("[Symbols] Loaded {Count} filtered symbols in {Elapsed} ms",
                selected.Count, stopwatch.Elapsed.TotalMilliseconds);

            await _queue.WaitForProcessingAsync(cancellationToken);

            // 6 Notifier que le download est terminé
            await new SymbolDownloadedEvent().PublishAsync(Mode.WaitForNone, cancellationToken);

            return selected.Count;
        }
        catch (Exception ex)
        {
            _isInFallback = true;
            _logger.LogError(ex, "[Symbols] Failed to load from Binance.");
            IsOnline = false;
            StartConnectivityMonitor(cancellationToken);
        }
        finally
        {
            _isLoading = false;
        }

        return 0;
    }


    public async Task<(int, List<SymbolMetadata>)> GetSymbols(string? exchange, string? quoteClass, string? currency, bool includeInactive, int page, int pageSize, string? sortBy)
    {
        int count = await _store.Count(exchange, quoteClass, includeInactive);

        if (count < 1)
        {
            _logger.LogWarning("[Symbols] No symbols found for query: Exchange={Exchange}, QuoteClass={QuoteClass}, Currency={Currency}, IncludeInactive={IncludeInactive}, Page={Page}, PageSize={PageSize}, SortBy={SortBy}",
                exchange, quoteClass, currency, includeInactive, page, pageSize, sortBy);

            return (0, new List<SymbolMetadata>());
        }
        else
        {
            _logger.LogInformation("[Symbols] Querying symbols: Exchange={Exchange}, QuoteClass={QuoteClass}, Currency={Currency}, IncludeInactive={IncludeInactive}, Page={Page}, PageSize={PageSize}, SortBy={SortBy}",
                exchange, quoteClass, currency, includeInactive, page, pageSize, sortBy);

            var symbols = await _store.Query(exchange, quoteClass, includeInactive, page, pageSize, sortBy);

            return (count, symbols);
        }
    }

    public async Task<int> TotalCount()
    {
        return await _store.Count(null, null, false);
    }
}