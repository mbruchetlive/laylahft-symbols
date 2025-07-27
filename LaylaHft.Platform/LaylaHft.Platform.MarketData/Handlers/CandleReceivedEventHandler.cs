using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Events;
using LaylaHft.Platform.MarketData.Options;
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

    private static readonly Meter _meter = new("LaylaHft.CandleDetection", "1.0");
    private static readonly Counter<int> _eventDetectedCounter = _meter.CreateCounter<int>("layla_marketdata_change_detected");

    // .NET Metrics (System.Diagnostics.Metrics) ne supporte pas decimal. Conversion explicite nécessaire.
    private static readonly Histogram<double> _ratioVolumeHistogram = _meter.CreateHistogram<double>("layla_ratio_volume", unit: "ratio");
    private static readonly Histogram<double> _ratioCloseHistogram = _meter.CreateHistogram<double>("layla_ratio_close", unit: "ratio");
    private static readonly Histogram<double> _ratioVolatilityHistogram = _meter.CreateHistogram<double>("layla_ratio_volatility", unit: "ratio");

    private static readonly ActivitySource ActivitySource = new("LaylaHft.CandleDetection");

    public async Task HandleAsync(CandleReceivedEvent e, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("HandleCandleReceived");
        var snapshot = e.Snapshot;

        logger.LogInformation("📩 Bougie reçue pour {Symbol} à {CloseTime}", snapshot.Symbol, snapshot.CloseTime);

        if (!bufferRegistry.IsInitialized(snapshot.Symbol, snapshot.Interval))
        {
            logger.LogWarning("⚠️ Buffer non initialisé pour {Symbol} / {Interval}", snapshot.Symbol, snapshot.Interval);
            return;
        }

        var buffer = bufferRegistry.GetBuffer(snapshot.Symbol, snapshot.Interval);

        if (buffer.Count < 20)
        {
            logger.LogWarning("⚠️ Buffer insuffisant pour {Symbol} / {Interval} : {Count} bougies", snapshot.Symbol, snapshot.Interval, buffer.Count);
            return;
        }

        var avgVolume = buffer.Average(c => c.Volume);
        var avgClose = buffer.Average(c => c.Close);
        var avgVolatility = buffer.Average(c => c.High - c.Low);

        logger.LogInformation("📊 Moyennes pour {Symbol}: Volume={AvgVolume}, Close={AvgClose}, Volatilité={AvgVolatility}", snapshot.Symbol, avgVolume, avgClose, avgVolatility);

        if (avgVolume == 0 || avgClose == 0 || avgVolatility == 0)
        {
            logger.LogWarning("⚠️ Moyenne nulle pour {Symbol}, impossible de calculer les ratios.", snapshot.Symbol);
            return;
        }

        var ratioVolume = snapshot.Volume / avgVolume;
        var ratioClose = snapshot.Close / avgClose;
        var ratioVolatility = (snapshot.High - snapshot.Low) / avgVolatility;

        _ratioVolumeHistogram.Record((double)ratioVolume);
        _ratioCloseHistogram.Record((double)ratioClose);
        _ratioVolatilityHistogram.Record((double)ratioVolatility);

        logger.LogInformation("📈 Ratios pour {Symbol} / {Interval}: Volume={RatioVolume}, Close={RatioClose}, Volatilité={RatioVolatility}",
            snapshot.Symbol, snapshot.Interval, ratioVolume, ratioClose, ratioVolatility);

        var changeTypes = new List<string>();

        if (ratioVolume > _settings.VolumeSpikeRatioThreshold && snapshot.Volume > _settings.MinVolume)
            changeTypes.Add("VolumeSpike");

        if (ratioClose > _settings.PriceJumpRatioThreshold)
            changeTypes.Add("PriceJump");

        if (ratioVolatility > _settings.VolatilitySpikeRatioThreshold)
            changeTypes.Add("VolatilitySpike");

        if (changeTypes.Any())
        {
            logger.LogInformation("⚡ Changement détecté pour {Symbol} à {CloseTime}: {ChangeTypes}", snapshot.Symbol, snapshot.CloseTime, string.Join(", ", changeTypes));

            _eventDetectedCounter.Add(1);

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
