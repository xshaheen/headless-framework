// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using MailKit.Security;

namespace Headless.Emails.Mailkit;

/// <summary>
/// Represents configuration options for the MailKit SMTP email sender.
/// </summary>
[PublicAPI]
public sealed class MailkitSmtpOptions
{
    /// <summary>Gets or sets the SMTP server hostname or IP address.</summary>
    public required string Server { get; set; }

    /// <summary>
    /// Gets or sets the SMTP username.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Gets or sets the SMTP password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the SMTP port. The default is 587.</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Gets or sets the TLS negotiation strategy. The default is <see cref="SecureSocketOptions.StartTls"/>.
    /// </summary>
    public SecureSocketOptions SocketOptions { get; set; } = SecureSocketOptions.StartTls;

    /// <summary>
    /// Gets or sets the connection and command timeout applied to each pooled SMTP client.
    /// The default is 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum number of SMTP connections retained in the pool. The default is 10.
    /// </summary>
    public int MaxPoolSize { get; set; } = 10;

    /// <summary>
    /// Gets a value indicating whether both <see cref="User"/> and <see cref="Password"/> are non-empty.
    /// </summary>
    public bool HasCredentials => !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"SMTP: {Server}:{Port} (User: {User ?? "anonymous"})";
    }
}

[UsedImplicitly]
internal sealed class MailkitSmtpOptionsValidator : AbstractValidator<MailkitSmtpOptions>
{
    public MailkitSmtpOptionsValidator()
    {
        RuleFor(x => x.Server).NotEmpty();
        RuleFor(x => x.Port).GreaterThan(0);
        RuleFor(x => x.SocketOptions).IsInEnum();
        // SmtpClient.Timeout is set from (int)Timeout.TotalMilliseconds; cap it to avoid int overflow.
        RuleFor(x => x.Timeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMilliseconds(int.MaxValue));
        RuleFor(x => x.MaxPoolSize).GreaterThanOrEqualTo(0);
    }
}
