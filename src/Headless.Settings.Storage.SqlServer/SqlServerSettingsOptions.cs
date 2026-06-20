// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.Settings.SqlServer;

/// <summary>Options for the SQL Server settings storage provider.</summary>
[PublicAPI]
public sealed class SqlServerSettingsOptions
{
    /// <summary>SQL Server connection string used to open connections for DDL and DML operations.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal SqlConnection CreateConnection() => new(ConnectionString);
}

/// <summary>Validates <see cref="SqlServerSettingsOptions"/> on startup.</summary>
internal sealed class SqlServerSettingsOptionsValidator : AbstractValidator<SqlServerSettingsOptions>
{
    public SqlServerSettingsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
