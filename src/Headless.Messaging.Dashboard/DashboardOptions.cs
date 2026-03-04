// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard.Authentication;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Represents all the option you can use to configure the dashboard.
/// </summary>
public class DashboardOptions
{
    /// <summary>
    /// When behind the proxy, specify the base path to allow spa call prefix.
    /// </summary>
    public string PathBase { get; set; } = string.Empty;

    /// <summary>
    /// Path prefix to match from url path.
    /// </summary>
    public string PathMatch { get; set; } = "/messaging";

    /// <summary>
    /// The interval the /stats endpoint should be polled with.
    /// </summary>
    public int StatsPollingInterval { get; set; } = 2000;

    /// <summary>
    /// Authentication configuration for the dashboard.
    /// </summary>
    public AuthConfig Auth { get; set; } = new();

    /// <summary>
    /// Explicitly allows anonymous access for the messaging dashboard API, passing AllowAnonymous to the ASP.NET Core global authorization filter.
    /// </summary>
    /// <remarks>Deprecated: use <see cref="Auth"/> instead.</remarks>
    public bool AllowAnonymousExplicit { get; set; } = false;

    /// <summary>
    /// Authorization policy for the Dashboard. Required if <see cref="AllowAnonymousExplicit"/> is false.
    /// </summary>
    /// <remarks>Deprecated: use <see cref="Auth"/> instead.</remarks>
    public string? AuthorizationPolicy { get; set; }
}
