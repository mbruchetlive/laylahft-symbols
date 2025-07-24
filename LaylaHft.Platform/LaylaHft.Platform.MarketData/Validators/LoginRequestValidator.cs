using FastEndpoints;
using FluentValidation;
using LaylaHft.Platform.Shared;

namespace LaylaHft.Platform.MarketData.Validators;

public class LoginRequestValidator : Validator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty()
            .WithMessage("ClientId is required.");

        RuleFor(x => x.ClientSecret)
            .NotEmpty()
            .WithMessage("ClientSecret is required.");

        RuleFor(x => x.Scopes)
            .NotNull()
            .WithMessage("Scopes are required.")
            .Must(scopes => scopes.Length > 0)
            .WithMessage("At least one scope must be provided.");
    }
}
