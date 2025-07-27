using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Events;
using LaylaHft.Platform.MarketData.Options;
using LaylaHft.Platform.MarketData.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace LaylaHft.Platform.MarketData.Handlers;

public class CandleReceivedEventHandler(ICandleBufferRegistry bufferRegistry,
    IOptions<MarketDetectionSettings> settings,
    ILogger<CandleReceivedEventHandler> logger,
    IHubContext<SymbolHub> hub 
    ) : IEventHandler<CandleReceivedEvent>
{
    private readonly MarketDetectionSettings _settings = settings.Value;
    private readonly IHubContext<SymbolHub> _hub = hub;

    public async Task HandleAsync(CandleReceivedEvent e, CancellationToken ct)
    {
        logger.LogInformation("Message reçu pour {Symbol} à {CloseTime}", e.Snapshot.Symbol, e.Snapshot.CloseTime);

        var snapshot = e.Snapshot;

        if (!bufferRegistry.IsInitialized(snapshot.Symbol, e.Snapshot.Interval))
        {
            logger.LogWarning("Le buffer pour {Symbol} / {Interval} n’est pas initialisé.", snapshot.Symbol, e.Snapshot.Interval);
            return;
        }

        var buffer = bufferRegistry.GetBuffer(snapshot.Symbol, e.Snapshot.Interval);

        if (buffer.Count < 20)
        {
            logger.LogWarning("Le buffer pour {Symbol} / {Interval} contient moins de 20 bougies.", snapshot.Symbol, e.Snapshot.Interval);
            return;
        }

        var avgVolume = buffer.Average(c => c.Volume);
        var avgClose = buffer.Average(c => c.Close);
        var avgVolatility = buffer.Average(c => c.High - c.Low);

        logger.LogInformation("Volume moyen: {AvgVolume}, Close moyen: {AvgClose}, Volatilité moyenne: {AvgVolatility}", avgVolume, avgClose, avgVolatility);

        if (avgVolume == 0 || avgClose == 0 || avgVolatility == 0)
        {
            logger.LogWarning("Impossible de calculer les ratios car une des moyennes est nulle pour {Symbol}.", snapshot.Symbol);
            return;
        }

        logger.LogInformation("Calcul des ratios pour {Symbol} / {Interval}", snapshot.Symbol, e.Snapshot.Interval);

        var ratioVolume = snapshot.Volume / avgVolume;
        var ratioClose = snapshot.Close / avgClose;
        var ratioVolatility = (snapshot.High - snapshot.Low) / avgVolatility;

        logger.LogInformation("Ratios calculés pour {Symbol} / {Interval} - Volume: {RatioVolume}, Close: {RatioClose}, Volatilité: {RatioVolatility}", 
            snapshot.Symbol, e.Snapshot.Interval, ratioVolume, ratioClose, ratioVolatility);

        logger.LogInformation("Analyse des changements pour {Symbol} à {CloseTime}", snapshot.Symbol, snapshot.CloseTime);

        var changeTypes = new List<string>();

        if (ratioVolume > _settings.VolumeSpikeRatioThreshold && snapshot.Volume > _settings.MinVolume)
            changeTypes.Add("VolumeSpike");

        if (ratioClose > _settings.PriceJumpRatioThreshold)
            changeTypes.Add("PriceJump");

        if (ratioVolatility > _settings.VolatilitySpikeRatioThreshold)
            changeTypes.Add("VolatilitySpike");

        if (changeTypes.Any())
        {
            logger.LogInformation("Changements détectés pour {Symbol} à {CloseTime}: {ChangeTypes}", snapshot.Symbol, snapshot.CloseTime, string.Join(", ", changeTypes));

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