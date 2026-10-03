// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Middlewares;
using Headless.Api.MultiTenancy;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api;

/// <summary>
/// Allows higher-level packages (e.g. <c>Headless.Api.ServiceDefaults</c>) to receive a callback when
/// <see cref="SetupMiddlewares.UseStatusCodesRewriter"/> is wired into the pipeline, without creating a
/// circular package dependency.
/// </summary>
/// <remarks>Framework coordination interface — not intended for direct consumer use.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IStatusCodesRewriterCalledNotifier
{
    /// <summary>Called immediately when <see cref="SetupMiddlewares.UseStatusCodesRewriter"/> is added to the pipeline.</summary>
    void OnCalled();
}
