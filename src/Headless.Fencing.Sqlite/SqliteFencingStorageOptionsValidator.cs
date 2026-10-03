// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Fencing.Sqlite;

/// <summary>
/// Checks the shared fencing schema name against PostgreSQL's identifier rules: SQLite accepts any quoted name, and
/// the dialect's snake_case convention is PostgreSQL's.
/// </summary>
internal sealed class SqliteFencingStorageOptionsValidator : AbstractValidator<FencingStorageOptions>
{
    public SqliteFencingStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
