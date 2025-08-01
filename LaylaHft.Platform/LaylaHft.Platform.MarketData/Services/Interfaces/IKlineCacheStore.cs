using Binance.Net.Interfaces;
using LaylaHft.Platform.Domains;
using System.Collections.Concurrent;

namespace LaylaHft.Platform.MarketData.Services.Interfaces
{
    public interface IKlineCacheStore
    {
        ConcurrentDictionary<string, List<IBinanceKline>> Cache { get; }

        List<IBinanceKline>? GetKlines(string symbol, string interval);
        void SetKlines(string symbol, string interval, List<IBinanceKline> candles);
    }
}