// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Headless.EntityFramework;

/// <summary>Options for one tenant-routed context type registered with <c>RouteTenantData&lt;TContext&gt;()</c>.</summary>
[PublicAPI]
public sealed class TenantDataRoutingOptions
{
    /// <summary>
    /// How many tenant schemas keep their EF model (and compiled queries) cached for this context type before the
    /// least recently used are evicted and rebuilt on next use. Each cached schema holds one runtime and one
    /// design-time model plus up to about 100 compiled queries. Default: 100.
    /// </summary>
    public int MaxCachedSchemas { get; set; } = 100;

    /// <summary>
    /// The routed data store this context belongs to: the <see cref="TenantDataPlacementRequest.DataStore"/> the
    /// placement resolver is asked for. Name one per group of contexts a tenant may place differently (orders in one
    /// database, billing in another); contexts that share a data store share a tenant's placement. Default:
    /// <see cref="TenantDataPlacementRequest.DefaultDataStore"/>.
    /// </summary>
    public string DataStore { get; set; } = TenantDataPlacementRequest.DefaultDataStore;
}
