// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// Marks an endpoint that a dashboard's own credential (Basic or API key) protects. The dashboard's
/// authentication scheme authenticates only requests routed to an endpoint carrying its own name.
/// </summary>
/// <param name="AuthenticationName">The dashboard's authentication name, which is also its scheme name.</param>
internal sealed record DashboardEndpointMetadata(string AuthenticationName);
