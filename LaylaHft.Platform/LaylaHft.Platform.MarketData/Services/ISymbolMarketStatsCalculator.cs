using LaylaHft.Platform.Domains;

namespace LaylaHft.Platform.MarketData.Services;

public interface ISymbolMarketStatsCalculator
{
    Task CalculateAsync(SymbolMetadata symbol);
}
