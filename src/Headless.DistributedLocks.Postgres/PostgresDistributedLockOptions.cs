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
}

internal sealed class PostgresDistributedLockOptionsValidator : AbstractValidator<PostgresDistributedLockOptions>
{
    public PostgresDistributedLockOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.PollingFallback).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(30));
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMinutes(10));
        RuleFor(x => x)
            .Must(x => x.DataSource is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage($"{nameof(PostgresDistributedLockOptions.ConnectionString)} or {nameof(PostgresDistributedLockOptions.DataSource)} is required.");
    }
}
