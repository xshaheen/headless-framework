// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

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
    /// Explicitly allows anonymous access for the messaging dashboard API, passing AllowAnonymous to the ASP.NET Core global authorization filter.
    /// </summary>
    public bool AllowAnonymousExplicit { get; set; } = true;

    /// <summary>
    /// Authorization policy for the Dashboard. Required if <see cref="AllowAnonymousExplicit"/> is false.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }
}
