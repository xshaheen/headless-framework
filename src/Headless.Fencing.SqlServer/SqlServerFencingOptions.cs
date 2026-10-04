// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Fencing.SqlServer;

/// <summary>Connection and command options for the SQL Server fencing provider.</summary>
/// <remarks>
/// <see cref="RelationalFencingOptions.ConnectionString" /> is the SqlClient connection string of the database that holds the leases; name the database explicitly (<c>Initial Catalog</c>). The schema that holds the lease table is
/// shared by every fencing provider and configured through <see cref="FencingStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class SqlServerFencingOptions : RelationalFencingOptions;

internal sealed class SqlServerFencingOptionsValidator()
    : RelationalFencingOptionsValidator<SqlServerFencingOptions>("SQL Server");

/// <summary>Checks the shared fencing schema name against SQL Server's identifier rules.</summary>
internal sealed class SqlServerFencingStorageOptionsValidator : AbstractValidator<FencingStorageOptions>
{
    public SqlServerFencingStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.SqlServer);
    }
}
