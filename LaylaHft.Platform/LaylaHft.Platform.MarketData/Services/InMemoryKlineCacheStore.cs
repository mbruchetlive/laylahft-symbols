using Binance.Net.Interfaces;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Services.Interfaces;
using System.Collections.Concurrent;

namespace LaylaHft.Platform.MarketData.Services;

public class InMemoryKlineCacheStore : IKlineCacheStore
{
    public ConcurrentDictionary<string, List<IBinanceKline>> Cache { get; } = new ConcurrentDictionary<string, List<IBinanceKline>>();

    public void SetKlines(string symbol, string interval, List<IBinanceKline> candles)
    {
        var key = $"{symbol}:{interval}";
        Cache[key] = candles;
    }

    public List<IBinanceKline>? GetKlines(string symbol, string interval)
    {
        var key = $"{symbol}:{interval}";

        if (Cache.TryGetValue(key, out var candles))
        {
            return candles;
        }

        return null;
    }
}
