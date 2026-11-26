// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Fencing;

/// <summary>The operator view over the provider's lease store: validates each call and pages the store's reads.</summary>
internal sealed class FencedLeaseInspector(ILeaseStore store) : IFencedLeaseInspector
{
    public async ValueTask<FencedLeasePage> ListAsync(
        FencedLeaseQuery query,
        FencedLeasePosition? after,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var filter = LeaseRequestResolver.ResolveQuery(query);
        Argument.IsInclusiveBetween(pageSize, 1, FencedLeasePage.MaxPageSize);
        var cursor = after is null ? (LeaseKey?)null : LeaseRequestResolver.ResolvePosition(after);

        // One lease past the page proves there is another page without a second round trip.
        var rows = await store.ListAsync(filter, cursor, pageSize + 1, cancellationToken).ConfigureAwait(false);

        if (rows.Count <= pageSize)
        {
            return new FencedLeasePage { Items = rows };
        }

        var items = rows.Take(pageSize).ToList();

        return new FencedLeasePage { Items = items, Next = FencedLeasePosition.Of(items[^1]) };
    }

    public async ValueTask<LeaseSnapshot?> GetAsync(
        string? tenantId,
        string kind,
        string resource,
        CancellationToken cancellationToken = default
    )
    {
        var key = LeaseRequestResolver.ResolveExplicit(tenantId, kind, resource);

        return await store.GetCurrentAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<string>> ListKindsAsync(CancellationToken cancellationToken = default)
    {
        return store.ListKindsAsync(cancellationToken);
    }

    public async ValueTask<FencedLeaseCounts> CountAsync(
        FencedLeaseQuery query,
        int cap = LeaseCount.DefaultCap,
        CancellationToken cancellationToken = default
    )
    {
        var filter = LeaseRequestResolver.ResolveQuery(query);
        // The store reads one lease past the cap, so the cap must leave room for it.
        Argument.IsInclusiveBetween(cap, 1, int.MaxValue - 1);

        return await store.CountAsync(filter, cap, cancellationToken).ConfigureAwait(false);
    }
}
