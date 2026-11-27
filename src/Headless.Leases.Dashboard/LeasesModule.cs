// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Leases;

/// <summary>
/// The Leases dashboard module: the fenced leases the fencing store holds and the distributed locks the lock provider
/// lists, with a sidebar count and exact-id lookup. It declares no commands and writes to neither store.
/// </summary>
/// <remarks>
/// Its object kinds are <c>fenced-lease</c> (a <c>{tenant}/{kind}/{resource}</c> id) and the held-lock kind (a
/// resource name), so its list topics are <c>leases.fenced-lease</c> and <c>leases.lock</c>. Nothing publishes on
/// them: the views refetch on demand.
/// </remarks>
internal sealed class LeasesModule : IDashboardModule, IDashboardCountSource, IDashboardObjectResolver
{
    private readonly FencedLeaseEndpoints _fenced;
    private readonly LockEndpoints _locks;
    private readonly LeasesSources _sources;

    public LeasesModule(LeasesServices services, LeaseKindsCache kinds, TimeProvider timeProvider)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(kinds);
        Argument.IsNotNull(timeProvider);

        _fenced = new FencedLeaseEndpoints(services, kinds, timeProvider);
        _locks = new LockEndpoints(services, timeProvider);
        _sources = new LeasesSources(Id, services);
    }

    public string Id => LeasesDashboardOptions.ModuleId;

    public string DisplayName => "Leases";

    public DashboardModuleAssets Assets { get; } = LeasesAssets.Create();

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _fenced.Map(endpoints);
        _locks.Map(endpoints);
    }

    public Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    )
    {
        return _sources.GetCountsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        return _sources.ResolveAsync(request.Query, cancellationToken);
    }
}
