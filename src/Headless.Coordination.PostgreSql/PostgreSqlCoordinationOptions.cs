// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Coordination.PostgreSql;

[PublicAPI]
public sealed class PostgreSqlCoordinationOptions
{
    public string? ConnectionString { get; set; }

    public NpgsqlDataSource? DataSource { get; set; }

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool InitializeOnStartup { get; set; } = true;

    internal NpgsqlConnection CreateConnection()
    {
        return DataSource is not null ? DataSource.CreateConnection() : new NpgsqlConnection(ConnectionString);
    }
}

internal sealed class PostgreSqlCoordinationOptionsValidator : AbstractValidator<PostgreSqlCoordinationOptions>
{
    public PostgreSqlCoordinationOptionsValidator()
    {
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMinutes(10));
        RuleFor(x => x)
            .Must(x => x.DataSource is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                $"{nameof(PostgreSqlCoordinationOptions.ConnectionString)} or {nameof(PostgreSqlCoordinationOptions.DataSource)} is required."
            );
    }
}
