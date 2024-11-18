// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using MailKit.Security;

namespace Framework.Emails.Mailkit;

public sealed class MailkitSmtpOptions
{
    public required string Server { get; init; }

    public string? User { get; init; }

    public string? Password { get; init; }

    public int Port { get; init; } = 25;

    public SecureSocketOptions? SocketOptions { get; init; }

    public bool RequiresAuthentication => !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);
}

internal sealed class MailkitSmtpOptionsValidator : AbstractValidator<MailkitSmtpOptions>
{
    public MailkitSmtpOptionsValidator()
    {
        RuleFor(x => x.Server).NotEmpty();
        RuleFor(x => x.Port).GreaterThan(0);
        RuleFor(x => x.SocketOptions).IsInEnum();
    }
}
