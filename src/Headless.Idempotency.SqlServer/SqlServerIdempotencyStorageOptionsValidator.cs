// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Idempotency.SqlServer;

/// <summary>Checks the shared idempotency schema name against SQL Server's identifier rules.</summary>
internal sealed class SqlServerIdempotencyStorageOptionsValidator : AbstractValidator<IdempotencyStorageOptions>
{
    public SqlServerIdempotencyStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.SqlServer);
    }
}
