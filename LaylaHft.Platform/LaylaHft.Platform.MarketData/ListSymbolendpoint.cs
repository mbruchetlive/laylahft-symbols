using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Requests;
using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.MarketData.Services;
using LaylaHft.Platform.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace LaylaHft.Platform.AppHost;

public class ListSymbolEndpoint : Endpoint<ListSymbolsQuery, ListSymbolResponse>
{
    public SymbolDownloader symbolDownloader { get; set; }
    public override void Configure()
    {
        Post("/api/symbols/query");

        Scopes("symbols:read");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);

        ResponseCache(1800);

        Throttle(
            hitLimit: 120,
            durationSeconds: 60,
            headerName: "X-Client-Id"
        );

        Description(b => b
        .Accepts<ListSymbolsQuery>("application/json")
        .Produces<ListSymbolResponse>(200, "application/json")
        .ProducesProblemFE(400)
        .ProducesProblemFE<InternalErrorResponse>(500),
        clearDefaults: true);

        Summary(s => {
            s.Summary = "Récupère une liste paginée de symboles disponibles sur un exchange donné.";
            s.Description = "Cet endpoint permet de récupérer une liste filtrée, triée et paginée de symboles de trading pour un exchange donné. Il prend en compte la classe de cotation, la devise, l’inclusion des symboles inactifs, le tri et la pagination. L'accès est sécurisé par un token JWT et limité via throttling.";

            s.ExampleRequest = new ListSymbolsQuery
            {
                Exchange = "BINANCE",
                QuoteClass = "USDC",
                Currency = "USDC",
                IncludeInactive = false,
                Page = 1,
                PageSize = 20,
                SortBy = "symbol"
            };

            s.ResponseExamples[200] = new ListSymbolResponse
            {
                Success = true,
                Message = "success",
                Exchange = "BINANCE",
                QuoteClass = "USDC",
                Currency = "USDC",
                IncludeInactive = false,
                Page = 1,
                PageSize = 20,
                SortBy = "symbol",
                TotalCount = 2,
                Symbols = new List<SymbolMetadata>
        {
            new SymbolMetadata { Symbol = "BTCUSDC", Status = SymbolStatus.Active },
            new SymbolMetadata { Symbol = "ETHUSDC", Status = SymbolStatus.Active }
        }
            };

            s.Responses[200] = "Retourne la liste des symboles correspondant aux critères, paginée et triée.";
            s.Responses[403] = "Accès interdit : l'appelant n'a pas les autorisations suffisantes ou le token est invalide.";
        });

    }

    public override async Task HandleAsync(ListSymbolsQuery req, CancellationToken ct)
    {
        if (symbolDownloader.IsLoading)
            await Send.OkAsync(new ListSymbolResponse { Success = false, Message = "pending" }, ct);
        else if (await symbolDownloader.TotalCount() < 1)
            await Send.OkAsync(new ListSymbolResponse { Success = false, Message = "no symbols" }, ct);
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
