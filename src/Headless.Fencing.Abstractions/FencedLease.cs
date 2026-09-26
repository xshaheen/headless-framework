// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// One granted attempt on a lease: the lease identity plus the generation the grant issued. The generation is the
/// fencing token: every later renew, settle, release, or fence names it, and the store refuses the call once a newer
/// grant replaced it.
/// </summary>
/// <remarks>
/// A plain value, safe to persist or hand to another process: an executor that received the tenant, kind, resource,
/// and generation can rebuild the lease and settle it. Holding a <see cref="FencedLease" /> proves nothing by itself;
/// only the store's answer does.
/// </remarks>
/// <param name="TenantId">The owning tenant, or <see langword="null" /> for the host scope.</param>
/// <param name="Kind">The lease kind, which groups leases for sweeping and purging.</param>
/// <param name="Resource">The leased resource within the kind.</param>
/// <param name="Generation">The generation this grant issued; strictly greater than any earlier grant's.</param>
[PublicAPI]
public sealed record FencedLease(string? TenantId, string Kind, string Resource, long Generation);
