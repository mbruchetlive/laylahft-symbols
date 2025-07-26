using Binance.Net.Enums;
using LaylaHft.Platform.Domains;

namespace LaylaHft.Platform.MarketData.Services;

public interface ICandleBufferRegistry
{
    void InitializeBuffer(string symbol, KlineInterval interval, int windowSize);
    void Append(string symbol, KlineInterval interval, CandleSnapshot candle);
    IReadOnlyList<CandleSnapshot> GetBuffer(string symbol, KlineInterval interval);
    void Clear(string symbol, KlineInterval interval);
    void ClearAll();
    bool IsInitialized(string symbol, KlineInterval interval);
}
