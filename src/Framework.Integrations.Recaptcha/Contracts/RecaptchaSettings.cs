using FluentValidation;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Integrations.Recaptcha;

public sealed class RecaptchaSettings
{
    /// <summary>Google Recaptcha Secret Key.</summary>
    public required string SecretKey { get; set; }

    /// <summary>Google Recaptcha Site Key.</summary>
    public required string SiteKey { get; set; }
}

public sealed class RecaptchaSettingsValidator : AbstractValidator<RecaptchaSettings>
{
    public RecaptchaSettingsValidator()
    {
        RuleFor(x => x.SecretKey).NotEmpty();
        RuleFor(x => x.SiteKey).NotEmpty();
    }
}
