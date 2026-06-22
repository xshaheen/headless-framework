// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.Features.SqlServer;

/// <summary>Options for the SQL Server features storage provider.</summary>
[PublicAPI]
public sealed class SqlServerFeaturesOptions
{
    /// <summary>SQL Server connection string used to connect to the features database.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal SqlConnection CreateConnection() => new(ConnectionString);
}

/// <summary>Validates <see cref="SqlServerFeaturesOptions"/>.</summary>
internal sealed class SqlServerFeaturesOptionsValidator : AbstractValidator<SqlServerFeaturesOptions>
{
    public SqlServerFeaturesOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
