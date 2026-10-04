// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Idempotency;

/// <summary>Connection and command options every relational idempotency provider shares.</summary>
/// <remarks>
/// The schema that holds the record table is shared by every idempotency provider and configured through
/// <see cref="IdempotencyStorageOptions" />.
/// </remarks>
[PublicAPI]
public abstract class RelationalIdempotencyOptions
{
    /// <summary>
    /// Gets or sets the connection string of the database that holds the idempotency records. Autonomous calls,
    /// renewals, peeks, and the purge open their own connections with it, and an enlisted call is accepted only on a
    /// unit whose connection reaches the same database, so name the database explicitly. Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timeout of every command this provider runs. Default: 30 seconds. An admission waits for
    /// whichever transaction holds the key's record, so this also bounds how long a concurrent admission of the same
    /// key waits behind an open enlisted admission, fence, or completion.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether the schema, record table, index, and generation sequence are created at host startup when
    /// missing. Default: <see langword="true" />. Set it to <see langword="false" /> when a migration tool owns them;
    /// the provider never creates them lazily inside a call, because DDL inside a caller's transaction would roll back
    /// with it.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    internal int CommandTimeoutSeconds => (int)Math.Ceiling(CommandTimeout.TotalSeconds);
}

internal abstract class RelationalIdempotencyOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalIdempotencyOptions
{
    protected RelationalIdempotencyOptionsValidator(string engine)
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage($"A {engine} connection string is required.");
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
    }
}
