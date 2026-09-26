// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
}
