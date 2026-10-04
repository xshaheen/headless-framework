// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Idempotency.SqlServer;

/// <summary>Connection and command options for the SQL Server idempotency provider.</summary>
/// <remarks>
/// <see cref="RelationalIdempotencyOptions.ConnectionString" /> is the SqlClient connection string of the database
/// that holds the records; name the database explicitly (<c>Initial Catalog</c>). The schema that holds the record
/// table is shared by every idempotency provider and configured through <see cref="IdempotencyStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class SqlServerIdempotencyOptions : RelationalIdempotencyOptions;

internal sealed class SqlServerIdempotencyOptionsValidator()
    : RelationalIdempotencyOptionsValidator<SqlServerIdempotencyOptions>("SQL Server");

/// <summary>Checks the shared idempotency schema name against SQL Server's identifier rules.</summary>
internal sealed class SqlServerIdempotencyStorageOptionsValidator : AbstractValidator<IdempotencyStorageOptions>
{
    public SqlServerIdempotencyStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.SqlServer);
    }
}
