using LaylaHft.Platform.Domains;
using System.Collections.Concurrent;

namespace LaylaHft.Platform.MarketData.Services;

public class SymbolStore : ISymbolStore
{
    private readonly ConcurrentDictionary<string, SymbolMetadata> _symbols = new();

    public Task<int> Count(string? exchange, string? quoteClass, bool includeInactive)
    {
        var query = _symbols.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(exchange))
            query = query.Where(s => s.Exchange?.Equals(exchange, StringComparison.OrdinalIgnoreCase) == true);

        if (!string.IsNullOrEmpty(quoteClass))
            query = query.Where(s => s.QuoteAsset?.Equals(quoteClass, StringComparison.OrdinalIgnoreCase) == true);

        if (includeInactive)
            query = query.Where(s => s.Status == SymbolStatus.Inactive || s.Status == SymbolStatus.Active);

        return Task.FromResult(query.Count());
    }

    public Task<List<SymbolMetadata>> Query(string? exchange, string? quoteClass, bool includeInactive, int page, int pageSize, string? sortBy)
    {
        var query = _symbols.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(exchange))
            query = query.Where(s => s.Exchange?.Equals(exchange, StringComparison.OrdinalIgnoreCase) == true);

        if (!string.IsNullOrEmpty(quoteClass))
            query = query.Where(s => s.QuoteAsset?.Equals(quoteClass, StringComparison.OrdinalIgnoreCase) == true);

        if (includeInactive)
            query = query.Where(s => s.Status == SymbolStatus.Inactive || s.Status == SymbolStatus.Active);

        if (!string.IsNullOrEmpty(sortBy))
        {
            query = sortBy.ToLowerInvariant() switch
            {
                "symbol" => query.OrderBy(s => s.Symbol),
                "name" => query.OrderBy(s => s.Name),
                "exchange" => query.OrderBy(s => s.Exchange),
                "baseasset" => query.OrderBy(s => s.BaseAsset),
                "quoteasset" => query.OrderBy(s => s.QuoteAsset),
                _ => query
            };
        }
        else
        {
            query = query.OrderBy(s => s.Symbol); // Default sort by symbol
        }

        // Apply pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 100; // Default page size

        return Task.FromResult(query.Skip((page - 1) * pageSize).Take(pageSize).ToList());
    }

    public Task Upsert(string? exchange, string? quoteClass, string symbol, SymbolMetadata metadata)
    {
        var key = $"{exchange?.ToLowerInvariant()}|{quoteClass?.ToLowerInvariant()}|{symbol.ToLowerInvariant()}";
        _symbols[key] = metadata;
        return Task.CompletedTask;
    }
}
