// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard.Internal;

namespace Headless.DevInbox.Dashboard;

/// <summary>
/// The Dev inbox dashboard module: the emails, SMS, and push notifications a Development host captured instead of
/// sending, one channel per capture store the host registered.
/// </summary>
internal sealed class DevInboxModule : IDashboardModule
{
    private readonly DevInboxEndpoints _endpoints;

    public DevInboxModule(DevInboxStores stores)
    {
        Argument.IsNotNull(stores);

        _endpoints = new DevInboxEndpoints(stores);
    }

    public string Id => DevInboxDashboardOptions.ModuleId;

    public string DisplayName => "Dev inbox";

    public DashboardModuleAssets Assets { get; } = DevInboxAssets.Create();

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _endpoints.Map(endpoints);
    }
}
