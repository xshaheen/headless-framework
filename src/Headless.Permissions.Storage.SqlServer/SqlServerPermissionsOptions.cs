// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Permissions.SqlServer;

[PublicAPI]
public sealed class SqlServerPermissionsOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class SqlServerPermissionsOptionsValidator : AbstractValidator<SqlServerPermissionsOptions>
{
    public SqlServerPermissionsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}
