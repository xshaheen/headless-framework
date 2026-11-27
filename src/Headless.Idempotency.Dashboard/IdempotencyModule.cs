// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.MultiTenancy;
using Microsoft.Extensions.Logging;

namespace Headless.Idempotency;

/// <summary>
/// The Idempotency dashboard module: the store's settings and a lookup of one key's record metadata in a tenant the
/// operator names. It is read-only: it declares no command, no object kind, and no count, because releasing or
/// deleting a live attempt's record could run its operation twice, and keys cannot be listed or counted.
/// </summary>
/// <remarks>Its own API is mapped by <see cref="IdempotencyApi"/>.</remarks>
internal sealed class IdempotencyModule : IDashboardModule
{
    private readonly IdempotencyApi _api;

    public IdempotencyModule(
        IIdempotencyInspector inspector,
        ICurrentTenant currentTenant,
        ILogger<IdempotencyModule> logger
    )
    {
        Argument.IsNotNull(inspector);
        Argument.IsNotNull(currentTenant);
        Argument.IsNotNull(logger);

        _api = new IdempotencyApi(inspector, currentTenant, logger);
    }

    public string Id => IdempotencyDashboardOptions.ModuleId;

    public string DisplayName => "Idempotency";

    public DashboardModuleAssets Assets { get; } = IdempotencyAssets.Create();

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _api.Map(endpoints);
    }
}
