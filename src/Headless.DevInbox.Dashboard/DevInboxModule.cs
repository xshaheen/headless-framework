// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard.Internal;

namespace Headless.DevInbox.Dashboard;

/// <summary>
/// The Dev inbox dashboard module: the emails, SMS, and push notifications a Development host captured instead of
/// sending, one channel per capture store the host registered, with sidebar counts and id lookup.
/// </summary>
/// <remarks>
/// Its object kinds are <c>email</c>, <c>sms</c>, and <c>push</c>. A channel whose store the host did not register
/// serves no count, resolves nothing, and answers its routes with <c>404</c> <c>channel_unconfigured</c>.
/// </remarks>
internal sealed class DevInboxModule : IDashboardModule, IDashboardCountSource, IDashboardObjectResolver
{
    private readonly DevInboxChannels _channels;
    private readonly DevInboxEndpoints _endpoints;
    private readonly EmailContentEndpoints _content;

    public DevInboxModule(DevInboxStores stores)
    {
        Argument.IsNotNull(stores);

        _channels = new DevInboxChannels(stores);
        _endpoints = new DevInboxEndpoints(_channels);
        _content = new EmailContentEndpoints(_channels.Email);
    }

    public string Id => DevInboxDashboardOptions.ModuleId;

    public string DisplayName => "Dev inbox";

    public DashboardModuleAssets Assets { get; } = DevInboxAssets.Create();

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _endpoints.Map(endpoints);
        _content.Map(endpoints);
    }

    public Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    )
    {
        // An unregistered channel's key stays out, so the sidebar reads its count as unknown rather than zero. A store
        // holds at most its capacity, so no count is capped.
        IReadOnlyDictionary<string, DashboardCount> counts = _channels.All.ToDictionary(
            static channel => channel.CountKey,
            static channel => new DashboardCount { Count = channel.Count },
            StringComparer.Ordinal
        );

        return Task.FromResult(counts);
    }

    public Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        if (!DevInboxEndpoints.TryParseId(request.Query, out var id))
        {
            return Task.FromResult<IReadOnlyList<DashboardObject>>([]);
        }

        IReadOnlyList<DashboardObject> found =
        [
            .. _channels.All.Select(channel => channel.Describe(Id, id)).OfType<DashboardObject>(),
        ];

        return Task.FromResult(found);
    }
}
