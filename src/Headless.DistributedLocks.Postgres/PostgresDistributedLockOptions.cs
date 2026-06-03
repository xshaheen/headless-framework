// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

[PublicAPI]
public sealed class PostgresDistributedLockOptions
{
    public string? ConnectionString { get; set; }

    public NpgsqlDataSource? DataSource { get; set; }

    public string KeyPrefix { get; set; } = DistributedLockOptions.DefaultKeyPrefix;

    public TimeSpan PollingFallback { get; set; } = TimeSpan.FromMilliseconds(100);

    public bool EnablePushWakeup { get; set; } = true;

    /// <summary>
    /// Per-command timeout applied to every advisory-lock, release, count, fencing, and notify command.
    /// Bounds the wait independently of the supplied <see cref="DataSource"/> (whose own CommandTimeout
    /// may be 0/unbounded), so a half-open TCP connection cannot hang an acquire or release indefinitely.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// TCP keepalive interval applied when the provider builds its own <see cref="NpgsqlDataSource"/>
    /// from <see cref="ConnectionString"/> and the connection string does not already specify one.
    /// Connection-scoped locks detect a silently-dropped idle holder through Npgsql's
    /// <c>StateChange</c> event, which only fires promptly when keepalive probes are enabled. Set to
    /// <see cref="TimeSpan.Zero"/> to leave keepalive disabled (the Npgsql default). Ignored when an
    /// <see cref="DataSource"/> is injected — configure keepalive on that data source yourself.
    /// </summary>
    /// <remarks>
    /// This is an interim safeguard. An application-level connection monitor that actively probes the
    /// holding connection is a planned follow-up and will remove the dependency on keepalive timing.
    /// </remarks>
    public TimeSpan KeepAlive { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class PostgresDistributedLockOptionsValidator : AbstractValidator<PostgresDistributedLockOptions>
{
    public PostgresDistributedLockOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.PollingFallback).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(30));
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMinutes(10));
        RuleFor(x => x.KeepAlive).GreaterThanOrEqualTo(TimeSpan.Zero);
        RuleFor(x => x)
            .Must(x => x.DataSource is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage($"{nameof(PostgresDistributedLockOptions.ConnectionString)} or {nameof(PostgresDistributedLockOptions.DataSource)} is required.");
    }
}
