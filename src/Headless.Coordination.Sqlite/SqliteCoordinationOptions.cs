// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

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
