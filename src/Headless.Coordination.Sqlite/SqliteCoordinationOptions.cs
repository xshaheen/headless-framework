// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Coordination.Sqlite;

/// <summary>Connection and command options for the SQLite coordination backing store.</summary>
/// <remarks>
/// <see cref="RelationalCoordinationOptions.ConnectionString" /> is required. The schema that holds the membership
/// tables is configured through <see cref="CoordinationStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class SqliteCoordinationOptions : RelationalCoordinationOptions;

internal sealed class SqliteCoordinationOptionsValidator
    : RelationalCoordinationOptionsValidator<SqliteCoordinationOptions>
{
    public SqliteCoordinationOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A SQLite connection string is required.");
    }
}

/// <summary>Checks the shared coordination schema name against PostgreSQL's identifier rules: SQLite accepts any quoted name, and the dialect's snake_case convention is PostgreSQL's.</summary>
internal sealed class SqliteCoordinationStorageOptionsValidator : AbstractValidator<CoordinationStorageOptions>
{
    public SqliteCoordinationStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
