// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Fencing.PostgreSql;

/// <summary>Checks the shared fencing schema name against PostgreSQL's identifier rules.</summary>
internal sealed class PostgreSqlFencingStorageOptionsValidator : AbstractValidator<FencingStorageOptions>
{
    public PostgreSqlFencingStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
