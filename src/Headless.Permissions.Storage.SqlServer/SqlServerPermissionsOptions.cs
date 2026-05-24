// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Permissions.SqlServer;

[PublicAPI]
public sealed class SqlServerPermissionsOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

internal sealed class SqlServerPermissionsOptionsValidator : AbstractValidator<SqlServerPermissionsOptions>
{
    public SqlServerPermissionsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
