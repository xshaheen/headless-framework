// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Features.SqlServer;

[PublicAPI]
public sealed class SqlServerFeaturesOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

internal sealed class SqlServerFeaturesOptionsValidator : AbstractValidator<SqlServerFeaturesOptions>
{
    public SqlServerFeaturesOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
