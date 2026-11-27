// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.DistributedLocks;
using Headless.Fencing;

namespace Headless.Leases;

/// <summary>
/// The Leases module's shell contributions: the sidebar count and the palette's exact-id lookup. Neither writes, and
/// the lookup does no text search.
/// </summary>
internal sealed class LeasesSources(string moduleId, LeasesServices services)
{
    /// <summary>The sidebar count key: live fenced leases, or held locks on a host without Fencing.</summary>
    public const string CountKey = "leases";

    /// <summary>A fenced lease, by the id <see cref="FencedLeaseId"/> mints.</summary>
    public const string LeaseKind = "fenced-lease";

    /// <summary>A held distributed lock, by its resource name.</summary>
    public const string LockKind = "lock";

    /// <summary>The status a resolved lock carries: the provider lists only held locks.</summary>
    public const string HeldStatus = "Held";

    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly LeasesServices _services = Argument.IsNotNull(services);

    public async Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(CancellationToken cancellationToken)
    {
        DashboardCount count;

        if (_services.HasFencing)
        {
            var counts = await _services
                .Inspector.CountAsync(new FencedLeaseQuery { State = FencedLeaseStateFilter.Live }, cancellationToken)
                .ConfigureAwait(false);

            count = new DashboardCount { Count = counts.Live.Count, Capped = counts.Live.Capped };
        }
        else if (_services.HasLocks)
        {
            var held = await _services.Lock.GetActiveLocksCountAsync(cancellationToken).ConfigureAwait(false);
            count = new DashboardCount { Count = held };
        }
        else
        {
            return new Dictionary<string, DashboardCount>(StringComparer.Ordinal);
        }

        return new Dictionary<string, DashboardCount>(StringComparer.Ordinal) { [CountKey] = count };
    }

    public async Task<IReadOnlyList<DashboardObject>> ResolveAsync(string query, CancellationToken cancellationToken)
    {
        var objects = new List<DashboardObject>();

        if (string.IsNullOrWhiteSpace(query))
        {
            return objects;
        }

        if (_services.HasFencing && FencedLeaseId.TryParse(query, out var tenantId, out var kind, out var resource))
        {
            LeaseSnapshot? snapshot = null;

            try
            {
                snapshot = await _services
                    .Inspector.GetAsync(tenantId, kind, resource, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // A string shaped like an id whose parts no lease can carry is simply not a match.
            }

            if (snapshot is not null)
            {
                objects.Add(
                    new DashboardObject
                    {
                        Target = _Ref(
                            LeaseKind,
                            FencedLeaseId.Format(snapshot.Lease.TenantId, snapshot.Lease.Kind, snapshot.Lease.Resource)
                        ),
                        Title = snapshot.Lease.Resource,
                        Status = snapshot.Status.ToString(),
                    }
                );
            }
        }

        if (_services.HasLocks)
        {
            DistributedLockInfo? info = null;

            try
            {
                info = await _services.Lock.GetLockInfoAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // A provider may refuse a resource name it could never lock; that is not a match either.
            }

            if (info is not null)
            {
                objects.Add(
                    new DashboardObject
                    {
                        Target = _Ref(LockKind, info.Resource),
                        Title = info.Resource,
                        Status = HeldStatus,
                    }
                );
            }
        }

        return objects;
    }

    private ObjectRef _Ref(string kind, string id) =>
        new()
        {
            Module = _moduleId,
            Kind = kind,
            Id = id,
        };
}
