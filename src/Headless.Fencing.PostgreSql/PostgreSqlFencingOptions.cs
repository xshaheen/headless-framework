// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Fencing.PostgreSql;

/// <summary>Connection and command options for the PostgreSQL fencing provider.</summary>
/// <remarks>
/// <see cref="RelationalFencingOptions.ConnectionString" /> is the Npgsql connection string of the database that holds the leases. The schema that holds the lease table is
/// shared by every fencing provider and configured through <see cref="FencingStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class PostgreSqlFencingOptions : RelationalFencingOptions;

internal sealed class PostgreSqlFencingOptionsValidator()
    : RelationalFencingOptionsValidator<PostgreSqlFencingOptions>("PostgreSQL");

/// <summary>Checks the shared fencing schema name against PostgreSQL's identifier rules.</summary>
internal sealed class PostgreSqlFencingStorageOptionsValidator : AbstractValidator<FencingStorageOptions>
{
    public PostgreSqlFencingStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
