// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Coordination.SqlServer;

/// <summary>Connection and command options for the SQL Server coordination backing store.</summary>
/// <remarks>
/// <see cref="RelationalCoordinationOptions.ConnectionString" /> is required. The schema that holds the membership
/// tables is configured through <see cref="CoordinationStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class SqlServerCoordinationOptions : RelationalCoordinationOptions;

internal sealed class SqlServerCoordinationOptionsValidator
    : RelationalCoordinationOptionsValidator<SqlServerCoordinationOptions>
{
    public SqlServerCoordinationOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A SQL Server connection string is required.");
    }
}

/// <summary>Checks the shared coordination schema name against SQL Server's identifier rules.</summary>
internal sealed class SqlServerCoordinationStorageOptionsValidator : AbstractValidator<CoordinationStorageOptions>
{
    public SqlServerCoordinationStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.SqlServer);
    }
}
