// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.AuditLog;

/// <summary>Connection and command options every relational audit-log storage provider shares.</summary>
[PublicAPI]
public abstract class RelationalAuditLogOptions
{
    /// <summary>
    /// Gets or sets the connection string used to open connections for audit row writes and reads when the provider
    /// does not enlist in the caller's transaction. Required; validated non-empty on startup.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the timeout applied to every command this provider runs. Default: 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal int CommandTimeoutSeconds => (int)CommandTimeout.TotalSeconds;
}

internal abstract class RelationalAuditLogOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalAuditLogOptions
{
    protected RelationalAuditLogOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
