using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using LaylaHft.Platform.Domains;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace LaylaHft.Platform.MarketData.Services;

public class InMemorySymbolMarketStatsCalculator : ISymbolMarketStatsCalculator
{
    private readonly IBinanceRestClient _client;
    private readonly ISymbolStore _store;
    private readonly IHubContext<SymbolHub> _hub;
    private readonly ILogger<InMemorySymbolMarketStatsCalculator> _logger;
    private readonly IKlineCacheStore _klineCacheStore;
    private static readonly Meter _meter;
    private static readonly Counter<int> _successCounter;
    private static readonly Counter<int> _failureCounter;
    private static readonly Histogram<double> _durationHistogram;
    private static readonly ActivitySource _activitySource;

    public List<string> Timeframes { get; }

    static InMemorySymbolMarketStatsCalculator()
    {
        // Register the ActivitySource for telemetry
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        _meter = new Meter("LaylaHft.MarketStats", "1.0");
        _activitySource = new ActivitySource("LaylaHft.MarketStats");

        _successCounter = _meter.CreateCounter<int>("stats.calculated.success.count", description: "Nombre de symboles calculés avec succès");
        _failureCounter = _meter.CreateCounter<int>("stats.calculated.failure.count", description: "Nombre de symboles ayant échoué");
        _durationHistogram = _meter.CreateHistogram<double>("stats.calculation.duration.ms", unit: "ms", description: "Temps de calcul des stats marché");
    }

    public InMemorySymbolMarketStatsCalculator(
        IBinanceRestClient client,
        ISymbolStore store,
        IHubContext<SymbolHub> hub,
        IKlineCacheStore klineCacheStore,
        IConfiguration configuration,
        ILogger<InMemorySymbolMarketStatsCalculator> logger)
    {
        _client = client;
        _store = store;
        _hub = hub;
        _logger = logger;
        _klineCacheStore = klineCacheStore;

        var timeframes = configuration.GetSection("Timeframes").Get<List<string>>();
        
        if (timeframes == null || timeframes.Count == 0)
            throw new InvalidOperationException("Configuration invalide : section 'Timeframes' absente ou vide.");

        Timeframes = timeframes;

    }

    public async Task CalculateAsync(SymbolMetadata symbol)
    {
        using var activity = _activitySource.StartActivity("CalculateStats", ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("⏳ Démarrage du calcul des stats pour {Symbol}", symbol.Symbol);

            var klines = new Dictionary<string, IBinanceKline[]>();

            // 📌 Chargement des klines pour chaque interval
            foreach (var interval in Timeframes)
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(
                        symbol.Symbol,
                        ConvertToKlineInterval(interval),
                        limit: 30);

                    if (result.Success && result.Data.Length > 0)
                    {
                        _klineCacheStore.SetKlines(symbol.Symbol, interval, result.Data.ToList());
                        klines[interval] = result.Data;
                        break;
                    }

                    _logger.LogWarning("Tentative {Attempt}/3 échouée pour {Symbol} interval {Interval}. Erreur : {Error}",
                        attempt, symbol.Symbol, interval, result.Error?.Message);

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }

            decimal lastPrice;
            List<decimal> closesD1 = new();

            // 📌 Fallback si D1 absent
            if (klines.TryGetValue("D1", out var d1Klines) && d1Klines.Length > 0)
            {
                closesD1 = d1Klines.Select(k => k.ClosePrice).ToList();
                lastPrice = closesD1.Last();
            }
            else if (klines.TryGetValue("H1", out var h1Klines) && h1Klines.Length >= 24)
            {
                _logger.LogWarning("D1 manquant pour {Symbol}, fallback sur H1", symbol.Symbol);
                closesD1 = h1Klines.TakeLast(24).Select(k => k.ClosePrice).ToList();
                lastPrice = closesD1.Last();
            }
            else
            {
                _logger.LogWarning("⏭ Pas de données suffisantes pour {Symbol} (ni D1 ni H1)", symbol.Symbol);
                return;
            }

            // 📊 Calculs
            symbol.CurrentPrice = lastPrice;
            symbol.Change24hPct = closesD1.Count >= 2 ? ComputePct(lastPrice, closesD1[^2]) : 0;
            symbol.Change7dPct = closesD1.Count >= 8 ? ComputePct(lastPrice, closesD1[^8]) : 0;
            symbol.Change30dPct = closesD1.Count >= 30 ? ComputePct(lastPrice, closesD1[0]) : 0;

            // 📈 Sparkline
            symbol.Sparkline = new SparklineData
            {
                Data = closesD1,
                Color = (closesD1.Count >= 30 ? symbol.Change30dPct : symbol.Change7dPct) >= 0 ? "green" : "red"
            };

            // 💾 Stockage et notification
            await _store.Upsert(symbol.Exchange, symbol.QuoteAsset, symbol.Symbol, symbol);
            await _hub.Clients.All.SendAsync("SymbolUpdated", symbol);

            _successCounter.Add(1);

            _logger.LogInformation("✅ Calcul terminé pour {Symbol} | Prix: {Price}, 24h: {Change24h}%",
                symbol.Symbol, lastPrice, symbol.Change24hPct);
        }
        catch (Exception ex)
        {
            _failureCounter.Add(1);
            _logger.LogError(ex, "❌ Échec du calcul des stats pour {Symbol}", symbol.Symbol);
        }
        finally
        {
            stopwatch.Stop();
            _durationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static KlineInterval ConvertToKlineInterval(string tf) => tf.ToUpperInvariant() switch
    {
        "M1" => KlineInterval.OneMinute,
        "M5" => KlineInterval.FiveMinutes,
        "M15" => KlineInterval.FifteenMinutes,
        "H1" => KlineInterval.OneHour,
        "H4" => KlineInterval.FourHour,
        "D1" => KlineInterval.OneDay,
        _ => throw new ArgumentException($"Timeframe non supporté : {tf}")
    };


    private static decimal ComputePct(decimal current, decimal reference)
        => reference != 0 ? Math.Round(((current - reference) / reference) * 100, 2) : 0;
}


