// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using MailKit.Security;

namespace Headless.Emails.Mailkit;

/// <summary>
/// Configuration options for the MailKit SMTP email sender.
/// </summary>
public sealed class MailkitSmtpOptions
{
    /// <summary>The SMTP server hostname or IP address.</summary>
    public required string Server { get; set; }

    /// <summary>
    /// The SMTP username. Together with <see cref="Password"/>, determines whether
    /// authenticated SMTP is used. Leave <see langword="null"/> for anonymous relay.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// The SMTP password. Store in user-secrets or a key vault — do not commit to source control.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>The SMTP port. Defaults to <c>587</c> (submission with STARTTLS).</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// The TLS/SSL negotiation strategy. Defaults to <see cref="SecureSocketOptions.StartTls"/>
    /// (opportunistic upgrade on port 587).
    /// </summary>
    public SecureSocketOptions SocketOptions { get; set; } = SecureSocketOptions.StartTls;

    /// <summary>
    /// Connection and command timeout applied to each pooled SMTP client.
    /// Defaults to <c>30</c> seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of SMTP connections retained in the pool. Defaults to <c>10</c>.
    /// Set to <c>0</c> to disable pooling (each send creates and tears down its own connection).
    /// </summary>
    public int MaxPoolSize { get; set; } = 10;

    /// <summary>
    /// <see langword="true"/> when both <see cref="User"/> and <see cref="Password"/> are non-empty,
    /// indicating that authenticated SMTP should be used.
    /// </summary>
    public bool HasCredentials => !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);

    /// <inheritdoc/>
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
        RuleFor(x => x.MaxPoolSize).GreaterThanOrEqualTo(0);
    }
}
