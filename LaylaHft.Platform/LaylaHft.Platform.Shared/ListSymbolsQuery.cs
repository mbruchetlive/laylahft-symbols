namespace LaylaHft.Platform.Shared;

/// <summary>
/// getSymbolsQuery is a query object used to filter and paginate through a list of financial symbols.
/// </summary>
public class ListSymbolsQuery
{
    /// <summary>
    /// gets or sets the exchange to filter symbols by.
    /// </summary>
    public string? Exchange { get; set; } = null;

    /// <summary>
    /// Gets or sets the symbol to filter by.
    /// </summary>
    public string? QuoteClass { get; set; } = null;

    /// <summary>
    /// Gets or sets the current for price and change.
    /// </summary>
    public string? Currency { get; set; } = null;

    /// <summary>
    /// gets or sets a value indicating whether to include inactive symbols in the results.
    /// </summary>
    public bool IncludeInactive { get; set; } = false;

    /// <summary>
    /// gets or sets the page number for pagination.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// gets or sets the number of items per page for pagination.
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// gets or sets the sorting order for the results.
    /// </summary>
    public string? SortBy { get; set; } = null;
}
