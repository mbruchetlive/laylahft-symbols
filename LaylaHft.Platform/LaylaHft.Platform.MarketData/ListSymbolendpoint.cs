using FastEndpoints;
using LaylaHft.Platform.Domains;
using LaylaHft.Platform.Shared;

namespace LaylaHft.Platform.AppHost;

public class ListSymbolendpoint : Endpoint<ListSymbolsQuery, ListSymbolResponse>
{
    public override void Configure()
    {
        Post("/api/symbols/query");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ListSymbolsQuery req, CancellationToken ct)
    {
        await Send.OkAsync(new ListSymbolResponse
        {
            Currency = req.Currency,
            Exchange = req.Exchange,
            IncludeInactive = req.IncludeInactive,
            Page = req.Page,
            PageSize = req.PageSize,
            QuoteClass = req.QuoteClass,
            SortBy = req.SortBy,
            TotalCount = 5,
            Symbols =
            [
                new SymbolMetadata
                {
                  BaseAsset = "BTC",
                  Exchange = "Binance",
                  QuoteAsset = "USDT",
                  Symbol = "BTCUSDT",
                  IconUrl = "https://laylahft.com/images/crypto/btc.png",
                  CurrentPrice = 105000.00m,
                  Change24hPct = 0.05m,
                  Change30dPct = 0.10m,
                  Change7dPct = 0.07m,
                  MaxPrice = 110000.00m,
                  MinPrice = 95000.00m,
                  MaxQty = 10.0m,
                  MinQty = 0.001m,
                  MinNotional = 10.0m,
                  PricePrecision = 2,
                    QuantityPrecision = 3,
                    Status = SymbolStatus.Active,
                    Name = "Bitcoin",
                    Sparkline = new SparklineData
                    {
                        Data = new List<decimal> { 100000.00m, 102000.00m, 103000.00m, 104000.00m, 105000.00m },
                        Color = "#f7931a"
                    },
                    StepSize = 0.001m,
                    TickSize = 0.01m,
                },
                new SymbolMetadata
                {
                    BaseAsset = "ETH",
                    Exchange = "Binance",
                    QuoteAsset = "USDT",
                    Symbol = "ETHUSDT",
                    IconUrl = "https://laylahft.com/images/crypto/eth.png",
                    CurrentPrice = 3500.00m,
                    Change24hPct = 0.03m,
                    Change30dPct = 0.08m,
                    Change7dPct = 0.05m,
                    MaxPrice = 3600.00m,
                    MinPrice = 3400.00m,
                    MaxQty = 100.0m,
                    MinQty = 0.01m,
                    MinNotional = 10.0m,
                    PricePrecision = 2,
                    QuantityPrecision = 3,
                    Status = SymbolStatus.Active,
                    Name = "Ethereum",
                    Sparkline = new SparklineData
                    {
                        Data = new List<decimal> { 3400.00m, 3450.00m, 3475.00m, 3500.00m, 3525.00m },
                        Color = "#627eea"
                    },
                    StepSize = 0.01m,
                    TickSize = 0.01m,
                },
                new SymbolMetadata
                {
                    BaseAsset = "BNB",
                    Exchange = "Binance",
                    QuoteAsset = "USDT",
                    Symbol = "BNBUSDT",
                    IconUrl = "https://laylahft.com/images/crypto/bnb.png",
                    CurrentPrice = 400.00m,
                    Change24hPct = 0.02m,
                    Change30dPct = 0.06m,
                    Change7dPct = 0.04m,
                    MaxPrice = 420.00m,
                    MinPrice = 380.00m,
                    MaxQty = 50.0m,
                    MinQty = 0.01m,
                    MinNotional = 10.0m,
                    PricePrecision = 2,
                    QuantityPrecision = 3,
                    Status = SymbolStatus.Active,
                    Name = "Binance Coin",
                    Sparkline = new SparklineData
                    {
                        Data = new List<decimal> { 380.00m, 390.00m, 395.00m, 400.00m, 405.00m },
                        Color = "#f3ba2f"
                    },
                    StepSize = 0.01m,
                    TickSize = 0.01m,
                }
            ]
        }, ct);
    }
}
