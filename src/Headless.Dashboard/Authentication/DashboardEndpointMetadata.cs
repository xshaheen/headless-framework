// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// Marks an endpoint that a dashboard's own credential (Basic or API key) protects. The dashboard's
/// authentication scheme authenticates only requests routed to an endpoint carrying its own name.
/// </summary>
/// <param name="AuthenticationName">The dashboard's authentication name, which is also its scheme name.</param>
internal sealed record DashboardEndpointMetadata(string AuthenticationName)
{
    /// <summary>
    /// Gets a value indicating whether the endpoint also accepts the dashboard's session cookie, as the shell's
    /// endpoints do. A request that sends no credential then gets no result from the credential scheme instead of a
    /// logged failure, so a request that carries only the session is not reported as a failed sign-in.
    /// </summary>
    public bool AcceptsSession { get; init; }
}
