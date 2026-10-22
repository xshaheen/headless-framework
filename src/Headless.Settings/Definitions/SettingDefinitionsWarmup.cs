// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Settings;

/// <summary>
/// Loads the dynamic setting definitions into the store's in-process cache after the host starts, so the first request
/// that reads a setting does not wait on the database.
/// </summary>
/// <remarks>
/// The dynamic-store switch is an option, known only once options resolve, so the warm-up is always registered and
/// gates itself: with <see cref="SettingManagementOptions.IsDynamicSettingStoreEnabled"/> off it neither holds readiness
/// nor resolves the store, whose dependencies such a host may never have registered.
/// </remarks>
internal sealed class SettingDefinitionsWarmup(
    IServiceProvider services,
    IOptions<SettingManagementOptions> optionsAccessor
) : IWarmup
{
    public string Name => "SettingDefinitions";

    public bool GatesReadiness => optionsAccessor.Value.IsDynamicSettingStoreEnabled;

    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        GatesReadiness
            ? services.GetRequiredService<IDynamicSettingDefinitionStore>().GetAllAsync(cancellationToken)
            : Task.CompletedTask;
}
