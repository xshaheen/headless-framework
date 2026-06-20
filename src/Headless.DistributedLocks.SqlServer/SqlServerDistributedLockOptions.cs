// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.DistributedLocks.SqlServer;

[PublicAPI]
public sealed class SqlServerDistributedLockOptions
{
    public const string DefaultSchema = "dbo";

    public string? ConnectionString { get; set; }

    public string Schema { get; set; } = DefaultSchema;

    public string KeyPrefix { get; set; } = DistributedLockOptions.DefaultKeyPrefix;

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool EnableFencing { get; set; } = true;

    internal SqlConnection CreateConnection() => new(ConnectionString);
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
