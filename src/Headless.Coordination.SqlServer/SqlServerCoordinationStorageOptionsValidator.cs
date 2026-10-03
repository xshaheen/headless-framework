// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Coordination.SqlServer;

/// <summary>Checks the shared coordination schema name against SQL Server's identifier rules.</summary>
internal sealed class SqlServerCoordinationStorageOptionsValidator : AbstractValidator<CoordinationStorageOptions>
{
    public SqlServerCoordinationStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.SqlServer);
    }
}
