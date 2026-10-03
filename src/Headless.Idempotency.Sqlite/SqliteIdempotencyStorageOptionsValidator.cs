// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Idempotency.Sqlite;

/// <summary>
/// Checks the shared idempotency schema name against PostgreSQL's identifier rules: SQLite accepts any quoted name,
/// and the dialect's snake_case convention is PostgreSQL's.
/// </summary>
internal sealed class SqliteIdempotencyStorageOptionsValidator : AbstractValidator<IdempotencyStorageOptions>
{
    public SqliteIdempotencyStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
