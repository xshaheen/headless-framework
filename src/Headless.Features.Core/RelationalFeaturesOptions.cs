// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Features;

/// <summary>Connection and command options every relational features storage provider shares.</summary>
[PublicAPI]
public abstract class RelationalFeaturesOptions
{
    /// <summary>Gets or sets the connection string used to open connections for DDL and DML operations. Required.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the timeout applied to every command this provider runs. Default: 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal int CommandTimeoutSeconds => (int)CommandTimeout.TotalSeconds;
}

internal abstract class RelationalFeaturesOptionsValidator<TOptions> : AbstractValidator<TOptions>
    where TOptions : RelationalFeaturesOptions
{
    protected RelationalFeaturesOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
