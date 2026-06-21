// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.DistributedLocks.PostgreSql;

/// <summary>
/// Configuration options for the PostgreSQL advisory-lock distributed-lock provider. Exactly one of
/// <see cref="ConnectionString"/> or <see cref="DataSource"/> must be set; an injected
/// <see cref="DataSource"/> is used as-is and is never disposed by the provider.
/// </summary>
[PublicAPI]
public sealed class PostgresDistributedLockOptions
{
    /// <summary>
    /// Gets or sets the Npgsql connection string used to build the provider-owned
    /// <see cref="NpgsqlDataSource"/>. Ignored when <see cref="DataSource"/> is set.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets a caller-owned <see cref="NpgsqlDataSource"/> to use instead of building one from
    /// <see cref="ConnectionString"/>. When set, the provider uses this data source as-is and never
    /// disposes it; configure keepalive and pooling on this instance directly.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// Gets or sets the prefix prepended to every resource name before it is encoded into an
    /// advisory-lock key. Defaults to <see cref="DistributedLockOptions.DefaultKeyPrefix"/>. Must not
    /// be empty.
    /// </summary>
    public string KeyPrefix { get; set; } = DistributedLockOptions.DefaultKeyPrefix;

    /// <summary>
    /// Gets or sets the maximum duration a waiter sleeps between consecutive acquisition attempts when
    /// no push-based release signal arrives. Must be greater than <see cref="TimeSpan.Zero"/> and at
    /// most 30 seconds. Defaults to 100 ms.
    /// </summary>
    public TimeSpan PollingFallback { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets a value indicating whether the provider publishes and listens for release signals
    /// over a PostgreSQL <c>LISTEN/NOTIFY</c> channel. When <see langword="true"/> (the default), a
    /// background <c>LISTEN</c> loop wakes blocked acquirers immediately on release rather than waiting
    /// for the next polling interval. When <see langword="false"/>, only the local in-process signal
    /// and the polling fallback are used.
    /// </summary>
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
    /// Keepalive is complementary to the active connection monitor (which probes the holding connection
    /// with a bounded-timeout server-side query): keepalive makes <c>StateChange</c> surface a dead socket
    /// faster, while the monitor is the authoritative active death check.
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
            .WithMessage(
                $"{nameof(PostgresDistributedLockOptions.ConnectionString)} or {nameof(PostgresDistributedLockOptions.DataSource)} is required."
            );
    }
}
