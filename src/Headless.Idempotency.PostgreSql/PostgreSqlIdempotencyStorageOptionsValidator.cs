// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Idempotency.PostgreSql;

/// <summary>Checks the shared idempotency schema name against PostgreSQL's identifier rules.</summary>
internal sealed class PostgreSqlIdempotencyStorageOptionsValidator : AbstractValidator<IdempotencyStorageOptions>
{
    public PostgreSqlIdempotencyStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
