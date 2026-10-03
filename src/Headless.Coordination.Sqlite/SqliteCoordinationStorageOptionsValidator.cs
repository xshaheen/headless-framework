// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Coordination.Sqlite;

/// <summary>Checks the shared coordination schema name against PostgreSQL's identifier rules: SQLite accepts any quoted name, and the dialect's snake_case convention is PostgreSQL's.</summary>
internal sealed class SqliteCoordinationStorageOptionsValidator : AbstractValidator<CoordinationStorageOptions>
{
    public SqliteCoordinationStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
