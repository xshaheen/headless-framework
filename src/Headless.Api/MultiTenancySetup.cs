// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Api;

[PublicAPI]
public static class MultiTenancySetup
{
    /// <summary>
    /// Enables the framework multi-tenancy primitives and configures how HTTP tenant resolution should read tenant claims.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Optional tenant resolution configuration.</param>
    /// <returns>The same host application builder.</returns>
    public static IHostApplicationBuilder AddHeadlessMultiTenancy(
        this IHostApplicationBuilder builder,
        Action<MultiTenancyOptions>? configure = null
    )
    {
        var optionsBuilder = builder.Services.AddOptions<MultiTenancyOptions, MultiTenancyOptionsValidator>();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        builder.Services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        builder.Services.AddOrReplaceSingleton<ICurrentTenant, CurrentTenant>();

        return builder;
    }
}

/// <summary>Options for HTTP tenant resolution.</summary>
public sealed class MultiTenancyOptions
{
    /// <summary>Claim type to read tenant ID from. Defaults to <c>tenant_id</c>.</summary>
    public string ClaimType { get; set; } = UserClaimTypes.TenantId;
}

internal sealed class MultiTenancyOptionsValidator : AbstractValidator<MultiTenancyOptions>
{
    public MultiTenancyOptionsValidator()
    {
        RuleFor(x => x.ClaimType).NotEmpty();
    }
}
