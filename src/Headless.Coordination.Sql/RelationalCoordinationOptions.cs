// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Coordination;

/// <summary>Defines connection and command options shared across relational coordination providers.</summary>
/// <remarks>
/// The schema holding membership tables is configured through <see cref="CoordinationStorageOptions"/>.
/// </remarks>
[PublicAPI]
public abstract class RelationalCoordinationOptions
{
    /// <summary>Gets or sets the connection string of the database holding the membership tables.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the execution timeout for commands run by the provider. Must be positive and at most 10 minutes.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether the schema runner creates missing tables at startup.
    /// Defaults to <see langword="true"/>. Set to <see langword="false"/> when an external tool manages migrations.
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
