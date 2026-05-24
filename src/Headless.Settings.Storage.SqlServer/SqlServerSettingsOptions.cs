// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Settings.SqlServer;

[PublicAPI]
public sealed class SqlServerSettingsOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

internal sealed class SqlServerSettingsOptionsValidator : AbstractValidator<SqlServerSettingsOptions>
{
    public SqlServerSettingsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
