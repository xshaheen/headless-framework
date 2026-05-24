// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Features.SqlServer;

[PublicAPI]
public sealed class SqlServerFeaturesOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class SqlServerFeaturesOptionsValidator : AbstractValidator<SqlServerFeaturesOptions>
{
    public SqlServerFeaturesOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}
