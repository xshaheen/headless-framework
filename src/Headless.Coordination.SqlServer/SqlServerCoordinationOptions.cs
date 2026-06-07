// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.Coordination.SqlServer;

[PublicAPI]
public sealed class SqlServerCoordinationOptions
{
    public const string DefaultSchema = "dbo";

    public string? ConnectionString { get; set; }

    public string Schema { get; set; } = DefaultSchema;

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool InitializeOnStartup { get; set; } = true;

    internal SqlConnection CreateConnection()
    {
        return new SqlConnection(ConnectionString);
    }
}

internal sealed class SqlServerCoordinationOptionsValidator : AbstractValidator<SqlServerCoordinationOptions>
{
    public SqlServerCoordinationOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.Schema).IsValidSqlServerIdentifier();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMinutes(10));
    }
}
