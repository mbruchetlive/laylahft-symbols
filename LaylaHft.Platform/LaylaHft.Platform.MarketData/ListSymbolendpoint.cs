using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Services;
using LaylaHft.Platform.Shared;
using Microsoft.AspNetCore.Mvc;

namespace LaylaHft.Platform.AppHost;

public class ListSymbolendpoint : Endpoint<ListSymbolsQuery, ListSymbolResponse>
{
    public SymbolDownloader symbolDownloader { get; set; }
    public override void Configure()
    {
        Post("/api/symbols/query");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ListSymbolsQuery req, CancellationToken ct)
    {
        if (symbolDownloader.IsLoading)
            await Send.OkAsync(new ListSymbolResponse { Success = false, Message = "pending" });
        else if (await symbolDownloader.TotalCount() < 1)
            await Send.OkAsync(new ListSymbolResponse { Success = false, Message = "no symbols" });
        else
        {
            var symbols = await symbolDownloader.GetSymbols(req.Exchange, req.QuoteClass, req.Currency, req.IncludeInactive, req.Page, req.PageSize, req.SortBy);

            await Send.OkAsync(new ListSymbolResponse
            {
                Currency = req.Currency,
                Exchange = req.Exchange,
                IncludeInactive = req.IncludeInactive,
                Page = req.Page,
                PageSize = req.PageSize,
                QuoteClass = req.QuoteClass,
                SortBy = req.SortBy,
                TotalCount = symbols.Item1,
                Symbols = symbols.Item2
            }, ct);
        }
    }
}
