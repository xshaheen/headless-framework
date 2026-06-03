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
}

internal sealed class PostgresDistributedLockOptionsValidator : AbstractValidator<PostgresDistributedLockOptions>
{
    public PostgresDistributedLockOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.PollingFallback).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(30));
        RuleFor(x => x)
            .Must(x => x.DataSource is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage($"{nameof(PostgresDistributedLockOptions.ConnectionString)} or {nameof(PostgresDistributedLockOptions.DataSource)} is required.");
    }
}
