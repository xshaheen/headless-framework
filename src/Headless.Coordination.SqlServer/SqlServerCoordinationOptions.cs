// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.Coordination.SqlServer;

/// <summary>Options for the SQL Server coordination backing store.</summary>
[PublicAPI]
public sealed class SqlServerCoordinationOptions
{
    /// <summary>
    /// SQL Server connection string. Required; must not be null or empty.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// ADO.NET command timeout for all coordination store queries. Must be positive and at most 10 minutes.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <see langword="true"/> (default), an <c>IHostedService</c> initializer creates the
    /// coordination schema and tables at startup if they do not already exist. Set to
    /// <see langword="false"/> if the schema is managed externally (for example by a migration tool).
    /// </summary>
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
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMinutes(10));
    }
}
