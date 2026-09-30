// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Fencing;

/// <summary>Connection and command options every relational fencing provider shares.</summary>
/// <remarks>
/// The schema that holds the lease table is shared by every fencing provider and configured through
/// <see cref="FencingStorageOptions" />.
/// </remarks>
[PublicAPI]
public abstract class RelationalFencingOptions
{
    /// <summary>
    /// Gets or sets the connection string of the database that holds the leases. Autonomous calls and sweeps open their
    /// own connections with it, and an enlisted call is accepted only on a unit whose connection reaches the same
    /// database, so name the database explicitly. Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timeout of every command this provider runs. Default: 30 seconds. A fence read waits for
    /// whichever transaction holds the lease row, so this also bounds how long a grant waits behind an open fence.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether the schema, lease table, indexes, and generation sequence are created at host startup
    /// when missing. Default: <see langword="true" />. Set it to <see langword="false" /> when a migration tool owns
    /// them; the provider never creates them lazily inside a call, because DDL inside a caller's transaction would
    /// roll back with it.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    internal int CommandTimeoutSeconds => (int)Math.Ceiling(CommandTimeout.TotalSeconds);
}

internal abstract class RelationalFencingOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalFencingOptions
{
    protected RelationalFencingOptionsValidator(string engine)
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage($"A {engine} connection string is required.");
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
    }
}
