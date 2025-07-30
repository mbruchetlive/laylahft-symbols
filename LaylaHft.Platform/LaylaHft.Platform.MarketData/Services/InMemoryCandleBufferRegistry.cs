using Binance.Net.Enums;
using LaylaHft.Platform.Domains;
using System.Collections.Concurrent;

namespace LaylaHft.Platform.MarketData.Services;

public class InMemoryCandleBufferRegistry(IKlineCacheStore klineCacheStore, ILogger<InMemoryCandleBufferRegistry> logger) : ICandleBufferRegistry
{
    private readonly ConcurrentDictionary<(string Symbol, string Interval), CircularBuffer<CandleSnapshot>> _buffers = new();
    private readonly Dictionary<(string Symbol, string TF), CandleSnapshot> _lastCandle = [];

    public void InitializeBuffer(string symbol, string interval, int windowSize)
    {
        var key = (symbol.ToUpperInvariant(), interval);
        _buffers.TryAdd(key, new CircularBuffer<CandleSnapshot>(windowSize));

        var klines = klineCacheStore.GetKlines(symbol, interval);

        if (klines != null && klines.Count > 0)
        {
            foreach (var kline in klines)
            {
                var candle = new CandleSnapshot
                {
                    OpenTime = kline.OpenTime,
                    CloseTime = kline.CloseTime,
                    Open = kline.OpenPrice,
                    High = kline.HighPrice,
                    Low = kline.LowPrice,
                    Close = kline.ClosePrice,
                    Volume = kline.Volume
                };

                Append(symbol, interval, candle);
            }
        }
    }

    public void Append(string symbol, string interval, CandleSnapshot candle)
    {
        var key = (symbol.ToUpperInvariant(), interval);

        if (!_buffers.TryGetValue(key, out var buffer))
            throw new InvalidOperationException($"Le buffer pour {symbol} / {interval} n’est pas initialisé.");

        buffer.Add(candle);
    }

    public IReadOnlyList<CandleSnapshot> GetBuffer(string symbol, string interval)
    {
        var key = (symbol.ToUpperInvariant(), interval);

        if (_buffers.TryGetValue(key, out var buffer))
            return buffer.ToList();

        return Array.Empty<CandleSnapshot>();
    }

    public void Clear(string symbol, string interval)
    {
        var key = (symbol.ToUpperInvariant(), interval);
        _buffers.TryRemove(key, out _);
    }

    public void ClearAll()
    {
        _buffers.Clear();
    }

    public bool IsInitialized(string symbol, string interval)
    {
        var key = (symbol.ToUpperInvariant(), interval);
        return _buffers.ContainsKey(key);
    }

    public CandleSnapshot? UpdatePartialCandle(string symbol, string tf, CandleSnapshot snapshot)
    {
        var key = (symbol, tf);

        if (!_lastCandle.TryGetValue(key, out var lastCandle))
        {
            // Première bougie pour ce symbole/timeframe
            logger.LogDebug("Initialisation bougie {Symbol} {TF}", symbol, tf);
            _lastCandle[key] = snapshot;
            return null;
        }

        // 🔍 Si CloseTime a changé → ancienne bougie est close
        if (snapshot.CloseTime > lastCandle.CloseTime)
        {
            logger.LogInformation("Bougie close détectée {Symbol} {TF} - Close={Close}",
                symbol, tf, lastCandle.Close);

            var closedCandle = lastCandle;

            // 🔄 Remplacer par la nouvelle bougie
            _lastCandle[key] = snapshot;

            // Retourner la bougie close pour publication
            return closedCandle;
        }

        // 🔄 Sinon, mise à jour des données de la bougie en cours
        lastCandle.Open = snapshot.Open;
        lastCandle.High = Math.Max(lastCandle.High, snapshot.High);
        lastCandle.Low = Math.Min(lastCandle.Low, snapshot.Low);
        lastCandle.Close = snapshot.Close;
        lastCandle.Volume = snapshot.Volume;
        lastCandle.Weight = snapshot.Weight;

        return null; // Pas de clôture détectée
    }

}