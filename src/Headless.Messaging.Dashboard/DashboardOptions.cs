// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    /// Allows anonymous access to the messaging dashboard. Defaults to <c>false</c>.
    /// Must be set to <see langword="true"/> explicitly or <see cref="AuthorizationPolicy"/> must be configured.
    /// </summary>
    public bool AllowAnonymousExplicit { get; set; }

    /// <summary>
    /// Authorization policy name for the dashboard. Required when <see cref="AllowAnonymousExplicit"/> is <see langword="false"/>.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }
}
