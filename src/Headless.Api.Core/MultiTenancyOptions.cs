// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Checks;
using Headless.Constants;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>Options for HTTP tenant resolution.</summary>
[PublicAPI]
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
