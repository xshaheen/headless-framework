// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Features.PostgreSql;

[PublicAPI]
public sealed class PostgreSqlFeaturesOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public NpgsqlConnection CreateConnection() => new(ConnectionString);
}

internal sealed class PostgreSqlFeaturesOptionsValidator : AbstractValidator<PostgreSqlFeaturesOptions>
{
    public PostgreSqlFeaturesOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
