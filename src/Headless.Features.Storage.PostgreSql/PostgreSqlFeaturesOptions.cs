// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Features.PostgreSql;

/// <summary>Options for the PostgreSQL features storage provider.</summary>
[PublicAPI]
public sealed class PostgreSqlFeaturesOptions
{
    /// <summary>PostgreSQL connection string used to connect to the features database.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal NpgsqlConnection CreateConnection() => new(ConnectionString);
}

/// <summary>Validates <see cref="PostgreSqlFeaturesOptions"/>.</summary>
internal sealed class PostgreSqlFeaturesOptionsValidator : AbstractValidator<PostgreSqlFeaturesOptions>
{
    public PostgreSqlFeaturesOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
