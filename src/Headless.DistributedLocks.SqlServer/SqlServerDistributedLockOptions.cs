// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// Configuration options for the SQL Server distributed-lock provider. Bound via the options pattern;
/// validated on application startup by <c>SqlServerDistributedLockOptionsValidator</c> when registered
/// through the <see cref="SetupSqlServerDistributedLocks"/> extension members.
/// </summary>
[PublicAPI]
public sealed class SqlServerDistributedLockOptions
{
    /// <summary>Default SQL Server schema used for the fencing sequence object when <see cref="Schema"/> is not overridden.</summary>
    public const string DefaultSchema = "dbo";

    /// <summary>
    /// SQL Server connection string used to open dedicated connections for lock acquisition and the fencing
    /// sequence. Must not be <see langword="null"/> or empty; validated on startup.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// SQL Server schema that hosts the fencing sequence created by
    /// <see cref="SqlServerDistributedLocksStorageInitializer"/>. Defaults to <see cref="DefaultSchema"/>
    /// (<c>"dbo"</c>). Must be a valid SQL Server identifier; validated on startup.
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;

    /// <summary>
    /// Prefix prepended to every resource name before encoding. Allows multiple logical namespaces to
    /// coexist under the same SQL Server instance without colliding on <c>sp_getapplock</c> resources.
    /// Defaults to <see cref="DistributedLockOptions.DefaultKeyPrefix"/>. Must not be empty; validated on startup.
    /// </summary>
    public string KeyPrefix { get; set; } = DistributedLockOptions.DefaultKeyPrefix;

    /// <summary>
    /// ADO.NET command timeout applied to lock acquisition, release, and liveness-probe commands.
    /// Must be greater than <see cref="TimeSpan.Zero"/> and no greater than 10 minutes; validated on startup.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <see langword="true"/> (the default), the provider creates and uses a SQL Server sequence object
    /// to stamp each exclusive acquisition with a strictly-increasing fencing token. Set to
    /// <see langword="false"/> to disable sequence creation and token issuance entirely — handles will carry
    /// no fencing token.
    /// </summary>
    public bool EnableFencing { get; set; } = true;

    internal SqlConnection CreateConnection()
    {
        return new(ConnectionString);
    }
}

internal sealed class SqlServerDistributedLockOptionsValidator : AbstractValidator<SqlServerDistributedLockOptions>
{
    public SqlServerDistributedLockOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.Schema).IsValidSqlServerIdentifier();
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMinutes(10));
    }
}
