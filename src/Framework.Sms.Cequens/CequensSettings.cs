using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Cequens;

public sealed class CequensSettings
{
    public required string Uri { get; init; }

    public required string ApiKey { get; init; }

    public required string UserName { get; init; }

    public required string SenderName { get; init; }

    public required string Token { get; init; }
}

[UsedImplicitly]
internal sealed class CequensSettingsValidator : AbstractValidator<CequensSettings>
{
    public CequensSettingsValidator()
    {
        RuleFor(x => x.Uri).NotEmpty().HttpUrl();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.SenderName).NotEmpty();
        RuleFor(x => x.Token).NotEmpty();
    }
}
