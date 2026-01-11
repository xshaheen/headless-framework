// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using MailKit.Security;

namespace Framework.Emails.Mailkit;

public sealed class MailkitSmtpOptions
{
    public required string Server { get; init; }

    public string? User { get; init; }

    /// <summary>
    /// SMTP password. Use user-secrets or key vault in production.
    /// </summary>
    public string? Password { get; init; }

    public int Port { get; init; } = 587;

    public SecureSocketOptions SocketOptions { get; init; } = SecureSocketOptions.StartTls;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public bool HasCredentials => !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);

    public override string ToString() => $"SMTP: {Server}:{Port} (User: {User ?? "anonymous"})";
}

[UsedImplicitly]
internal sealed class MailkitSmtpOptionsValidator : AbstractValidator<MailkitSmtpOptions>
{
    public MailkitSmtpOptionsValidator()
    {
        RuleFor(x => x.Server).NotEmpty();
        RuleFor(x => x.Port).GreaterThan(0);
        RuleFor(x => x.SocketOptions).IsInEnum();
        RuleFor(x => x.Timeout).GreaterThan(TimeSpan.Zero);
    }
}
