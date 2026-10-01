// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Coordination;

/// <summary>Connection and command options every relational coordination provider shares.</summary>
/// <remarks>
/// The schema that holds the membership tables is shared by every relational provider and configured through
/// <see cref="CoordinationStorageOptions" />.
/// </remarks>
[PublicAPI]
public abstract class RelationalCoordinationOptions
{
    /// <summary>Gets or sets the connection string of the database that holds the membership tables.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timeout of every command the provider runs. Must be positive and at most 10 minutes. Default:
    /// 30 seconds.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether the schema runner creates the schema and membership tables at host startup when missing.
    /// Default: <see langword="true" />. Set it to <see langword="false" /> when a migration tool owns them.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    internal int CommandTimeoutSeconds => Math.Max(1, (int)Math.Ceiling(CommandTimeout.TotalSeconds));
}

internal abstract class RelationalCoordinationOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalCoordinationOptions
{
    protected RelationalCoordinationOptionsValidator()
    {
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMinutes(10));
    }
}
