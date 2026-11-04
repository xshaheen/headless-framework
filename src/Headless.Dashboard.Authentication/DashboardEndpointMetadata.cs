// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Marks an endpoint that a dashboard's own credential (Basic, API key, or custom) protects. The dashboard's
/// authentication scheme authenticates only requests routed to an endpoint carrying its own name.
/// </summary>
/// <param name="AuthenticationName">The dashboard's authentication name, which is also its scheme name.</param>
internal sealed record DashboardEndpointMetadata(string AuthenticationName);
