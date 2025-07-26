using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using LaylaHft.Platform.Domains;
using Microsoft.AspNetCore.SignalR;
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

    private static readonly Meter _meter;
    private static readonly Counter<int> _successCounter;
    private static readonly Counter<int> _failureCounter;
    private static readonly Histogram<double> _durationHistogram;
    private static readonly ActivitySource _activitySource;

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
        ILogger<InMemorySymbolMarketStatsCalculator> logger)
    {
        _client = client;
        _store = store;
        _hub = hub;
        _logger = logger;
    }

    public async Task CalculateAsync(SymbolMetadata symbol)
    {
        using var activity = _activitySource.StartActivity("CalculateStats", ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("⏳ Démarrage du calcul des stats pour {Symbol}", symbol.Symbol);

            IBinanceKline[] klines = Array.Empty<IBinanceKline>();

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(
                    symbol.Symbol,
                    KlineInterval.OneDay,
                    limit: 30);

                if (result.Success && result.Data.Length >= 2)
                {
                    klines = result.Data;
                    break;
                }

                _logger.LogWarning("Tentative {Attempt}/3 échouée pour {Symbol}. Erreur : {Error}",
                    attempt, symbol.Symbol, result.Error?.Message);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }

            if (klines.Length < 2)
            {
                _logger.LogWarning("⚠️ Échec du chargement des klines pour {Symbol} après 3 tentatives", symbol.Symbol);
                _failureCounter.Add(1);
                return;
            }

            var closes = klines.Select(k => k.ClosePrice).ToList();
            var last = closes[^1];
            symbol.CurrentPrice = last;

            symbol.Change24hPct = closes.Count >= 2 ? ComputePct(last, closes[^2]) : 0;
            symbol.Change7dPct = closes.Count >= 8 ? ComputePct(last, closes[^8]) : 0;
            symbol.Change30dPct = closes.Count >= 30 ? ComputePct(last, closes[0]) : 0;

            symbol.Sparkline = new SparklineData
            {
                Data = closes,
                Color = (closes.Count >= 30 ? symbol.Change30dPct : symbol.Change7dPct) >= 0 ? "green" : "red"
            };

            await _store.Upsert(symbol.Exchange, symbol.QuoteAsset, symbol.Symbol, symbol);
            await _hub.Clients.All.SendAsync("SymbolUpdated", symbol);

            _successCounter.Add(1);
            _logger.LogInformation("✅ Calcul terminé pour {Symbol} | Prix: {Price}, 24h: {Change24h}%",
                symbol.Symbol, last, symbol.Change24hPct);
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

    private static decimal ComputePct(decimal current, decimal reference)
        => reference != 0 ? Math.Round(((current - reference) / reference) * 100, 2) : 0;
}


