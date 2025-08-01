using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Events;
using LaylaHft.Platform.MarketData.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LaylaHft.Platform.MarketData.Handlers;

public class CandleReceivedEventHandler(
    ICandleBufferRegistry bufferRegistry,
    IOptions<MarketDetectionSettings> settings,
    ILogger<CandleReceivedEventHandler> logger,
    IHubContext<SymbolHub> hub
) : IEventHandler<CandleReceivedEvent>
{
    private readonly MarketDetectionSettings _settings = settings.Value;
    private readonly IHubContext<SymbolHub> _hub = hub;

    private static readonly Meter _meter = new("LaylaHft.CandleDetection");
    private static readonly Counter<int> _eventDetectedCounter = _meter.CreateCounter<int>("layla_marketdata_change_detected");

    private static readonly Histogram<double> _ratioVolumeHistogram = _meter.CreateHistogram<double>("layla_ratio_volume", unit: "ratio");
    private static readonly Histogram<double> _ratioCloseHistogram = _meter.CreateHistogram<double>("layla_ratio_close", unit: "ratio");
    private static readonly Histogram<double> _ratioVolatilityHistogram = _meter.CreateHistogram<double>("layla_ratio_volatility", unit: "ratio");
    private static readonly Histogram<int> _validCandlesHistogram = _meter.CreateHistogram<int>("layla_buffer_valid_candles", unit: "count");
    private static readonly Histogram<int> _eventsPerHourHistogram = _meter.CreateHistogram<int>("layla_events_per_hour", unit: "count");
    private static readonly Gauge<int> _currentHourEventGauge = _meter.CreateGauge<int>("layla_events_current_hour");

    // Stocke le nombre d’événements par symbole + intervalle
    private static readonly Dictionary<(string Symbol, string Interval), int> _eventsCurrentHourPerTF = new();
    private static DateTime _lastResetHour = DateTime.UtcNow;

    private static readonly ActivitySource ActivitySource = new("LaylaHft.CandleDetection");

    public async Task HandleAsync(CandleReceivedEvent e, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("HandleCandleReceived");
        var snapshot = e.Snapshot;

        logger.LogInformation("📩 Bougie reçue pour {Symbol} à {CloseTime} ({Interval})",
            snapshot.Symbol, snapshot.CloseTime, snapshot.Interval);

        // Vérifier l'initialisation
        if (!bufferRegistry.IsInitialized(snapshot.Symbol, snapshot.Interval))
        {
            logger.LogWarning("⚠️ Buffer non initialisé pour {Symbol}/{Interval}", snapshot.Symbol, snapshot.Interval);
            return;
        }

        var buffer = bufferRegistry.GetBuffer(snapshot.Symbol, snapshot.Interval);

        // Vérifier données valides
        var validCandles = buffer.Where(c => c.Volume > 0 && c.Close > 0 && (c.High - c.Low) > 0).ToList();
        _validCandlesHistogram.Record(validCandles.Count,
            new KeyValuePair<string, object?>("symbol", snapshot.Symbol),
            new KeyValuePair<string, object?>("interval", snapshot.Interval));

        if (validCandles.Count < 20)
        {
            logger.LogInformation("ℹ️ Données insuffisantes pour {Symbol}/{Interval} : {ValidCount} bougies valides",
                snapshot.Symbol, snapshot.Interval, validCandles.Count);
            return;
        }

        // Calculs moyens
        var avgVolume = validCandles.Average(c => c.Volume);
        var avgClose = validCandles.Average(c => c.Close);
        var avgVolatility = validCandles.Average(c => c.High - c.Low);

        if (avgVolume == 0 || avgClose == 0 || avgVolatility == 0)
        {
            logger.LogWarning("⚠️ Moyenne incohérente (0) pour {Symbol}, ratios ignorés.", snapshot.Symbol);
            return;
        }

        // Ratios
        var ratioVolume = snapshot.Volume / avgVolume;
        var ratioClose = snapshot.Close / avgClose;
        var ratioVolatility = (snapshot.High - snapshot.Low) / avgVolatility;

        _ratioVolumeHistogram.Record((double)ratioVolume,
            new KeyValuePair<string, object?>("symbol", snapshot.Symbol),
            new KeyValuePair<string, object?>("interval", snapshot.Interval));

        _ratioCloseHistogram.Record((double)ratioClose,
            new KeyValuePair<string, object?>("symbol", snapshot.Symbol),
            new KeyValuePair<string, object?>("interval", snapshot.Interval));

        _ratioVolatilityHistogram.Record((double)ratioVolatility,
            new KeyValuePair<string, object?>("symbol", snapshot.Symbol),
            new KeyValuePair<string, object?>("interval", snapshot.Interval));

        logger.LogInformation("📈 Ratios {Symbol}/{Interval}: Volume={RatioVolume}, Close={RatioClose}, Volatilité={RatioVolatility}",
            snapshot.Symbol, snapshot.Interval, ratioVolume, ratioClose, ratioVolatility);

        // 🔍 Seuils spécifiques au timeframe
        var tfThresholds = _settings.GetThresholdsForTF(snapshot.Interval);

        var changeTypes = new List<string>();
        if (ratioVolume > tfThresholds.VolumeSpikeRatioThreshold && snapshot.Volume > tfThresholds.MinVolume)
            changeTypes.Add("VolumeSpike");
        if (ratioClose > tfThresholds.PriceJumpRatioThreshold)
            changeTypes.Add("PriceJump");
        if (ratioVolatility > tfThresholds.VolatilitySpikeRatioThreshold)
            changeTypes.Add("VolatilitySpike");

        if (changeTypes.Any())
        {
            logger.LogInformation("⚡ Changement détecté pour {Symbol}/{Interval} à {CloseTime}: {ChangeTypes}",
                snapshot.Symbol, snapshot.Interval, snapshot.CloseTime, string.Join(", ", changeTypes));

            _eventDetectedCounter.Add(1,
                new KeyValuePair<string, object?>("symbol", snapshot.Symbol),
                new KeyValuePair<string, object?>("interval", snapshot.Interval));

            var key = (Symbol: snapshot.Symbol, Interval: snapshot.Interval.ToString());

            if (!_eventsCurrentHourPerTF.ContainsKey(key))
                _eventsCurrentHourPerTF[key] = 0;

            _eventsCurrentHourPerTF[key]++;

            _currentHourEventGauge.Record(_eventsCurrentHourPerTF[key],
                new KeyValuePair<string, object?>("symbol", key.Symbol),
                new KeyValuePair<string, object?>("interval", key.Interval));
        }

        // ✅ Vérification reset horaire déplacée hors bloc `changeTypes.Any()`
        if (DateTime.UtcNow.Hour != _lastResetHour.Hour)
        {
            foreach (var kvp in _eventsCurrentHourPerTF)
            {
                _eventsPerHourHistogram.Record(kvp.Value,
                    new KeyValuePair<string, object?>("symbol", kvp.Key.Symbol),
                    new KeyValuePair<string, object?>("interval", kvp.Key.Interval));

                // Vérification plage cible M1 par symbole
                if (kvp.Key.Interval.Equals("M1", StringComparison.OrdinalIgnoreCase))
                {
                    if (kvp.Value < 50)
                    {
                        logger.LogWarning("⚠️ Peu d'événements détectés pour {Symbol} M1 ({Count})", kvp.Key.Symbol, kvp.Value);
                    }
                    else if (kvp.Value > 100)
                    {
                        logger.LogInformation("✅ Nombre d'événements élevé pour {Symbol} M1 ({Count})", kvp.Key.Symbol, kvp.Value);
                    }
                }
            }

            _eventsCurrentHourPerTF.Clear();
            _lastResetHour = DateTime.UtcNow;
        }

        // Diffusion événement
        if (changeTypes.Any())
        {
            var evt = new MarketDataChange
            {
                Symbol = snapshot.Symbol,
                Timeframe = snapshot.Interval.ToString(),
                Timestamp = snapshot.CloseTime,
                ChangeTypes = changeTypes,
                Candle = snapshot,
                Context = "RealTimeDetection",
                Source = "ChangeDetector"
            };
            await _hub.Clients.Group(snapshot.Symbol).SendAsync("MarketDataChangeDetected", evt);
        }
    }
}
