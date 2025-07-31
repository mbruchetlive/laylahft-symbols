using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Events;
using LaylaHft.Platform.MarketData.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LaylaHft.Platform.MarketData.Handlers;

public class CandleReceivedEventHandler(ICandleBufferRegistry bufferRegistry,
    IOptions<MarketDetectionSettings> settings,
    ILogger<CandleReceivedEventHandler> logger,
    IHubContext<SymbolHub> hub
    ) : IEventHandler<CandleReceivedEvent>
{
    private readonly MarketDetectionSettings _settings = settings.Value;
    private readonly IHubContext<SymbolHub> _hub = hub;

    private static readonly Meter _meter = new("LaylaHft.CandleDetection");
    private static readonly Counter<int> _eventDetectedCounter = _meter.CreateCounter<int>("layla_marketdata_change_detected");

    // .NET Metrics (System.Diagnostics.Metrics) ne supporte pas decimal. Conversion explicite nécessaire.
    private static readonly Histogram<double> _ratioVolumeHistogram = _meter.CreateHistogram<double>("layla_ratio_volume", unit: "ratio");
    private static readonly Histogram<double> _ratioCloseHistogram = _meter.CreateHistogram<double>("layla_ratio_close", unit: "ratio");
    private static readonly Histogram<double> _ratioVolatilityHistogram = _meter.CreateHistogram<double>("layla_ratio_volatility", unit: "ratio");
    private static readonly Histogram<int> _validCandlesHistogram = _meter.CreateHistogram<int>("layla_buffer_valid_candles", unit: "count", description: "Nombre de bougies valides dans le buffer");
    private static readonly Histogram<int> _eventsPerHourHistogram = _meter.CreateHistogram<int>("layla_events_per_hour", unit: "count", description: "Nombre d'événements détectés par heure");
    private static readonly Gauge<int> _currentHourEventGauge = _meter.CreateGauge<int>("layla_events_current_hour", description: "Nombre d'événements détectés sur l'heure courante");

    private static readonly Dictionary<string, int> _eventsCurrentHourPerTF = new();
    private static DateTime _lastResetHour = DateTime.UtcNow;

    private static readonly ActivitySource ActivitySource = new("LaylaHft.CandleDetection");

    public async Task HandleAsync(CandleReceivedEvent e, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("HandleCandleReceived");
        var snapshot = e.Snapshot;

        logger.LogInformation("📩 Bougie reçue pour {Symbol} à {CloseTime} ({Interval})", snapshot.Symbol, snapshot.CloseTime, snapshot.Interval);

        // Vérifier l'initialisation
        if (!bufferRegistry.IsInitialized(snapshot.Symbol, snapshot.Interval))
        {
            logger.LogWarning("⚠️ Buffer non initialisé pour {Symbol} / {Interval}", snapshot.Symbol, snapshot.Interval);
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
            logger.LogInformation("ℹ️ Données insuffisantes pour {Symbol} / {Interval} : {ValidCount} bougies valides", snapshot.Symbol, snapshot.Interval, validCandles.Count);
            return;
        }

        // Calculs sur bougies valides
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

        _ratioVolumeHistogram.Record((double)ratioVolume);
        _ratioCloseHistogram.Record((double)ratioClose);
        _ratioVolatilityHistogram.Record((double)ratioVolatility);

        logger.LogInformation("📈 Ratios {Symbol}/{Interval}: Volume={RatioVolume}, Close={RatioClose}, Volatilité={RatioVolatility}",
            snapshot.Symbol, snapshot.Interval, ratioVolume, ratioClose, ratioVolatility);

        // 🔍 Récupérer les seuils spécifiques au timeframe
        var tfThresholds = _settings.GetThresholdsForTF(snapshot.Interval);

        // Détection
        var changeTypes = new List<string>();

        if (ratioVolume > tfThresholds.VolumeSpikeRatioThreshold && snapshot.Volume > tfThresholds.MinVolume)
            changeTypes.Add("VolumeSpike");

        if (ratioClose > tfThresholds.PriceJumpRatioThreshold)
            changeTypes.Add("PriceJump");

        if (ratioVolatility > tfThresholds.VolatilitySpikeRatioThreshold)
            changeTypes.Add("VolatilitySpike");

        if (changeTypes.Any())
        {
            logger.LogInformation("⚡ Changement détecté pour {Symbol} ({Interval}) à {CloseTime}: {ChangeTypes}",
                snapshot.Symbol, snapshot.Interval, snapshot.CloseTime, string.Join(", ", changeTypes));

            _eventDetectedCounter.Add(1);

            // Dans HandleAsync, juste après `_eventDetectedCounter.Add(1);`
            string tfKey = snapshot.Interval.ToString().ToUpperInvariant();

            // Incrémente compteur en mémoire
            if (!_eventsCurrentHourPerTF.ContainsKey(tfKey))
                _eventsCurrentHourPerTF[tfKey] = 0;
            _eventsCurrentHourPerTF[tfKey]++;

            // Met à jour Gauge Prometheus
            _currentHourEventGauge.Record(_eventsCurrentHourPerTF[tfKey],
                new KeyValuePair<string, object?>("interval", tfKey));

            // Vérifie si on a changé d'heure → reset + push histogram
            if (DateTime.UtcNow.Hour != _lastResetHour.Hour)
            {
                foreach (var kvp in _eventsCurrentHourPerTF)
                {
                    _eventsPerHourHistogram.Record(kvp.Value,
                        new KeyValuePair<string, object?>("interval", kvp.Key));

                    // 🔍 Détection plage cible pour M1
                    if (kvp.Key == "M1" && (kvp.Value < 50 || kvp.Value > 150))
                    {
                        logger.LogWarning("⚠️ M1 hors plage : {Count} événements/h (Cible : 50–150)", kvp.Value);
                    }
                }

                _eventsCurrentHourPerTF.Clear();
                _lastResetHour = DateTime.UtcNow;
            }

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
