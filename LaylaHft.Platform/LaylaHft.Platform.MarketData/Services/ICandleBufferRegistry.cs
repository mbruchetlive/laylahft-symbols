using Binance.Net.Enums;
using LaylaHft.Platform.Domains;

namespace LaylaHft.Platform.MarketData.Services;

public interface ICandleBufferRegistry
{
    void InitializeBuffer(string symbol, string interval, int windowSize);
    void Append(string symbol, string interval, CandleSnapshot candle);
    IReadOnlyList<CandleSnapshot> GetBuffer(string symbol, string interval);
    void Clear(string symbol, string interval);
    void ClearAll();
    bool IsInitialized(string symbol, string interval);
}
