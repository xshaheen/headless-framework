// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Permissions;

/// <summary>
/// Loads the dynamic permission definitions into the store's in-process cache after the host starts, so the first
/// request that checks a permission does not wait on the database.
/// </summary>
/// <remarks>
/// The dynamic-store switch is an option, known only once options resolve, so the warm-up is always registered and
/// gates itself: with <see cref="PermissionManagementOptions.IsDynamicPermissionStoreEnabled"/> off it neither holds
/// readiness nor resolves the store, whose dependencies such a host may never have registered.
/// </remarks>
internal sealed class PermissionDefinitionsWarmup(
    IServiceProvider services,
    IOptions<PermissionManagementOptions> optionsAccessor
) : IWarmup
{
    public string Name => "PermissionDefinitions";

    public bool GatesReadiness => optionsAccessor.Value.IsDynamicPermissionStoreEnabled;

    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        GatesReadiness
            ? services.GetRequiredService<IDynamicPermissionDefinitionStore>().GetGroupsAsync(cancellationToken)
            : Task.CompletedTask;
}
