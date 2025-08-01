using LaylaHft.Platform.Domains;

namespace LaylaHft.Platform.MarketData.Services.Interfaces;

public interface ISymbolMarketStatsCalculator
{
    Task CalculateAsync(SymbolMetadata symbol);
}
