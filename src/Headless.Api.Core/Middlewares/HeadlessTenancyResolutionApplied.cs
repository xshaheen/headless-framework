// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Middlewares;

/// <summary>
/// Marker feature set on the current HTTP request when <see cref="TenantResolutionMiddleware"/> has
/// executed for it. Consumed by <c>HeadlessApiExceptionHandler</c> to surface a runtime warning when
/// a <see cref="MissingTenantContextException"/> is raised on a request that never passed through
/// <c>UseHeadlessTenancy()</c>.
/// </summary>
internal sealed class HeadlessTenancyResolutionApplied
{
    public static HeadlessTenancyResolutionApplied Instance { get; } = new();

    private HeadlessTenancyResolutionApplied() { }
}
