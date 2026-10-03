// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headless.Api.ServiceDefaults;

/// <summary>Antiforgery defaults.</summary>
/// <remarks>
/// CSRF protection only applies to cookie-based authentication. For bearer-token / API-key / OAuth APIs the browser
/// does not auto-attach credentials cross-origin, so antiforgery is not needed and adds latency. Enable explicitly
/// when the API uses cookie-based auth (ASP.NET Core Identity cookies, Server-rendered MVC sessions, etc.).
/// Consumers wire the middleware themselves via <c>app.UseAntiforgery()</c> after <c>UseAuthentication()</c>/<c>UseAuthorization()</c>.
/// </remarks>
[PublicAPI]
public sealed class HeadlessServiceDefaultsAntiforgeryOptions
{
    /// <summary>Whether <c>AddHeadless()</c> should register the antiforgery service. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; set; }
}
