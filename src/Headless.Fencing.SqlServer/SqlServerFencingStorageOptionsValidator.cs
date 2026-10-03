// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Fencing.SqlServer;

/// <summary>Checks the shared fencing schema name against SQL Server's identifier rules.</summary>
internal sealed class SqlServerFencingStorageOptionsValidator : AbstractValidator<FencingStorageOptions>
{
    public SqlServerFencingStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.SqlServer);
    }
}
