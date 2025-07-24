using LaylaHft.Platform.Domains;

namespace LaylaHft.Platform.MarketData.Services;

public interface ISymbolStore
{
    Task<int> Count(string? exchange, string? quoteClass, bool includeInactive);
    Task<List<SymbolMetadata>> Query(string? exchange, string? quoteClass, bool includeInactive, int page, int pageSize, string? sortBy);
    Task Upsert(string? exchange, string? quoteClass, string symbol, SymbolMetadata metadata);
    Task SaveToFileAsync();
}