// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Idempotency.PostgreSql;

/// <summary>Connection and command options for the PostgreSQL idempotency provider.</summary>
/// <remarks>
/// <see cref="RelationalIdempotencyOptions.ConnectionString" /> is the Npgsql connection string of the database that
/// holds the records. The schema that holds the record table is shared by every idempotency provider and configured
/// through <see cref="IdempotencyStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class PostgreSqlIdempotencyOptions : RelationalIdempotencyOptions;

internal sealed class PostgreSqlIdempotencyOptionsValidator()
    : RelationalIdempotencyOptionsValidator<PostgreSqlIdempotencyOptions>("PostgreSQL");

/// <summary>Checks the shared idempotency schema name against PostgreSQL's identifier rules.</summary>
internal sealed class PostgreSqlIdempotencyStorageOptionsValidator : AbstractValidator<IdempotencyStorageOptions>
{
    public PostgreSqlIdempotencyStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
