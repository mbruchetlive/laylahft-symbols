using Binance.Net.Enums;
using LaylaHft.Platform.Domains;
using System.Collections.Concurrent;

namespace LaylaHft.Platform.MarketData.Services;

public class InMemoryCandleBufferRegistry : ICandleBufferRegistry
{
    private readonly ConcurrentDictionary<(string Symbol, string Interval), CircularBuffer<CandleSnapshot>> _buffers = new();

    public void InitializeBuffer(string symbol, string interval, int windowSize)
    {
        var key = (symbol.ToUpperInvariant(), interval);
        _buffers.TryAdd(key, new CircularBuffer<CandleSnapshot>(windowSize));
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
}