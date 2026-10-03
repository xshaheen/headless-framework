// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api.ServiceDefaults;

/// <summary>
/// Concrete <see cref="IStatusCodesRewriterCalledNotifier"/> that sets the
/// <see cref="HeadlessStartupState.UseStatusCodesRewriterCalled"/> flag on the singleton startup state when
/// <see cref="SetupMiddlewares.UseStatusCodesRewriter"/> is called.
/// </summary>
internal sealed class StatusCodesRewriterCalledNotifier(HeadlessStartupState state) : IStatusCodesRewriterCalledNotifier
{
    public void OnCalled()
    {
        state.UseStatusCodesRewriterCalled = true;
    }
}
