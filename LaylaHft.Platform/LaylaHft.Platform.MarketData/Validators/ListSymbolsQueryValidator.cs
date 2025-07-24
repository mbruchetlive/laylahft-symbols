namespace LaylaHft.Platform.MarketData.Validators;

using FastEndpoints;
using FluentValidation;
using LaylaHft.Platform.Shared;

public class ListSymbolsQueryValidator : Validator<ListSymbolsQuery>
{
    public ListSymbolsQueryValidator()
    {
        RuleFor(x => x.Exchange)
            .NotEmpty()
            .WithMessage("Exchange is required.");

        RuleFor(x => x.QuoteClass)
            .NotEmpty()
            .WithMessage("QuoteClass is required.")
            .Must(value => new[] { "USDC", "USDT", "BNB", "BTC", "EUR" }.Contains(value))
            .WithMessage("QuoteClass must be one of: USDC, USDT, BNB, BTC, EUR.");

        RuleFor(x => x.Page)
            .GreaterThan(0)
            .WithMessage("Page must be greater than 0.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 1000)
            .WithMessage("PageSize must be between 1 and 1000.");

        RuleFor(x => x.SortBy)
            .Must(value => string.IsNullOrWhiteSpace(value) ||
                   new[] { "symbol", "name", "exchange", "baseasset", "quoteasset" }
                   .Contains(value.ToLowerInvariant()))
            .WithMessage("SortBy must be one of: symbol, name, exchange, baseasset, quoteasset (case-insensitive).");
    }
}
