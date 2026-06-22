// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.Permissions.SqlServer;

/// <summary>
/// Connection and command options for the SQL Server permissions storage provider.
/// </summary>
[PublicAPI]
public sealed class SqlServerPermissionsOptions
{
    /// <summary>The SQL Server connection string used to connect to the database.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal SqlConnection CreateConnection() => new(ConnectionString);
}

internal sealed class SqlServerPermissionsOptionsValidator : AbstractValidator<SqlServerPermissionsOptions>
{
    public SqlServerPermissionsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
