namespace LaylaHft.Platform.MarketData.Services;

// US01 - Téléchargement initial des symboles via Binance.Net

/*
SPECIFICATION DE L’US01 : Téléchargement initial des symboles

Objectif :
Télécharger la liste complète des symboles de Binance dès le démarrage de l’application,
les filtrer, les transformer en objets métier (SymbolMetadata), et les stocker en mémoire (ConcurrentDictionary) pour le reste des composants.

Étapes fonctionnelles :
1. Utiliser la méthode GetExchangeInfoAsync() de Binance.Net
2. Filtrer les symboles actifs :
   - status == Trading
   - isSpotTradingAllowed == true
3. Pour chaque symbole valide :
   - Extraire les métadonnées : baseAsset, quoteAsset, symbol, précisions
   - Extraire les filtres : tickSize, stepSize, min/max qty, minNotional
   - Générer un objet SymbolMetadata
4. Ajouter le symbole au dictionnaire mémoire (clé = symbol)
5. Logger le nombre de symboles chargés
6. Émettre un événement via IEventDispatcher ("SymbolListInitialized")
7. En cas d’erreur, logger l’échec et relancer l’exception (ou fallback)

Critères d’acceptation :
- La liste contient uniquement les symboles actifs
- Chaque symbole est bien mappé et stocké
- L’événement de fin de chargement est émis
- Les erreurs sont tracées proprement
* */

/*
 * US 5 : Récupération en mode failback
 * En tant que DataAggregator, je veux lire les symboles depuis un cache local en cas d’échec, afin de continuer à servir des données aux services même sans accès réseau.
* */

/*
 * US 6 : Surveillance de la connectivité
 * En tant que opérateur, je veux voir dans les logs si le système fonctionne en mode failback, afin de réagir rapidement en cas de problème réseau/API.
 * */

/*
 * US 8 : Notification d'événements de mise à jour, 
 * En tant que module dépendant (API, WebSocket, moteur), je veux être notifié lorsqu’un symbole est ajouté ou mis à jour, afin de réagir en temps réel aux changements de données. je pense qu'il faut qu'on crée un signal-r hub et l'invoquer depuis le service downloader. 
* */
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Objects.Models.Spot;
using LaylaHft.Platform.Domains;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

public class SymbolDownloader
{
    private readonly IBinanceRestClient _client;
    private readonly ISymbolStore _store;
    private readonly ILogger<SymbolDownloader> _logger;
    private readonly IHubContext<SymbolHub> _hubContext;

    private volatile bool _isLoading = false;
    public bool IsLoading => _isLoading;
    private Timer? _reconnectTimer;
    private bool _isInFallback;

    public bool IsOnline { get; private set; }
    public DateTime LastDownloadDate { get; private set; }

    public void StartConnectivityMonitor()
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
                    await LoadInitialSymbolsAsync();
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "[Symbols] Failed to load from Binance. Entering FAILBACK MODE.");
                _isInFallback = true;
            }
        },
        null,
        TimeSpan.FromSeconds(10),    // Première tentative rapide
        TimeSpan.FromMinutes(1));    // Ensuite toutes les minutes
    }

    public SymbolDownloader(
        IBinanceRestClient client,
        ISymbolStore store,
        IHubContext<SymbolHub> hubContext,
        ILogger<SymbolDownloader> logger)
    {
        _client = client;
        _store = store;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task LoadInitialSymbolsAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        _isLoading = true;

        try
        {
            var result = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync();
            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("Failed to get exchange info from Binance: {Error}", result.Error);
                throw new Exception("Binance API failed");
            }

            var tradable = result.Data.Symbols
                .Where(s => s.Status == Binance.Net.Enums.SymbolStatus.Trading && s.IsSpotTradingAllowed)
                .ToList();

            var importedCount = 0;

            foreach (var s in tradable)
            {
                var priceFilter = s.Filters.OfType<BinanceSymbolPriceFilter>().FirstOrDefault();
                var lotFilter = s.Filters.OfType<BinanceSymbolLotSizeFilter>().FirstOrDefault();
                var notionalFilter = s.Filters.OfType<BinanceSymbolMinNotionalFilter>().FirstOrDefault();

                var metadata = new SymbolMetadata
                {
                    Symbol = s.Name,
                    Name = $"{s.BaseAsset} / {s.QuoteAsset}",
                    Exchange = "Binance",
                    BaseAsset = s.BaseAsset,
                    QuoteAsset = s.QuoteAsset,
                    PricePrecision = s.QuoteAssetPrecision,
                    QuantityPrecision = s.BaseAssetPrecision,
                    TickSize = priceFilter?.TickSize ?? 0,
                    MinPrice = priceFilter?.MinPrice ?? 0,
                    MaxPrice = priceFilter?.MaxPrice ?? 0,
                    StepSize = lotFilter?.StepSize ?? 0,
                    MinQty = lotFilter?.MinQuantity ?? 0,
                    MaxQty = lotFilter?.MaxQuantity ?? 0,
                    MinNotional = notionalFilter?.MinNotional ?? 0,
                    CurrentPrice = 0,
                    Change24hPct = 0,
                    Change7dPct = 0,
                    Change30dPct = 0,
                    Sparkline = new(),
                    IconUrl = $"https://cdn.exemple.com/assets/icons/{s.BaseAsset}.svg"
                };

                await _store.Upsert(metadata.Exchange, metadata.QuoteAsset, metadata.Symbol, metadata);

                await _hubContext.Clients.All.SendAsync("SymbolUpdated", metadata);

                importedCount++;
            }

            await _store.SaveToFileAsync();

            stopwatch.Stop();
            _logger.LogInformation("[Symbols] Loaded {Count} symbols from Binance in {Elapsed} ms.", importedCount, stopwatch.Elapsed.TotalMilliseconds);

            IsOnline = true;
            LastDownloadDate = DateTime.Now;
        }
        catch (Exception ex)
        {
            _isInFallback = true;
            _logger.LogError(ex, "[Symbols] Failed to load from Binance.");
            IsOnline = false;
            StartConnectivityMonitor();
        }
        finally
        {
            _isLoading = false;
        }
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