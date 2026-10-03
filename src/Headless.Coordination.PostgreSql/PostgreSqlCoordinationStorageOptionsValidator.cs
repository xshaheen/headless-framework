// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Npgsql;

namespace Headless.Coordination.PostgreSql;

/// <summary>Checks the shared coordination schema name against PostgreSQL's identifier rules.</summary>
internal sealed class PostgreSqlCoordinationStorageOptionsValidator : AbstractValidator<CoordinationStorageOptions>
{
    public PostgreSqlCoordinationStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
