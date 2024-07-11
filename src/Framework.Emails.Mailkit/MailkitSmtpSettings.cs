using FluentValidation;
using MailKit.Security;

namespace Framework.Emails.Mailkit;

internal sealed class MailkitSmtpSettings
{
    public required string Server { get; init; }

    public string? User { get; init; }

    public string? Password { get; init; }

    public int Port { get; init; } = 25;

    public SecureSocketOptions? SocketOptions { get; init; }

    public bool RequiresAuthentication => !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);
}

internal sealed class MailkitSmtpSettingsValidator : AbstractValidator<MailkitSmtpSettings>
{
    public MailkitSmtpSettingsValidator()
    {
        RuleFor(x => x.Server).NotEmpty();
        RuleFor(x => x.Port).GreaterThan(0);
        RuleFor(x => x.SocketOptions).IsInEnum();
    }
}
