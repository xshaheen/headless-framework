// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Features;

/// <summary>
/// Loads the dynamic feature definitions into the store's in-process cache after the host starts, so the first request
/// that checks a feature does not wait on the database.
/// </summary>
/// <remarks>
/// The dynamic-store switch is an option, known only once options resolve, so the warm-up is always registered and
/// gates itself: with <see cref="FeatureManagementOptions.IsDynamicFeatureStoreEnabled"/> off it neither holds readiness
/// nor resolves the store, whose dependencies such a host may never have registered.
/// </remarks>
internal sealed class FeatureDefinitionsWarmup(
    IServiceProvider services,
    IOptions<FeatureManagementOptions> optionsAccessor
) : IWarmup
{
    public string Name => "FeatureDefinitions";

    public bool GatesReadiness => optionsAccessor.Value.IsDynamicFeatureStoreEnabled;

    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        GatesReadiness
            ? services.GetRequiredService<IDynamicFeatureDefinitionStore>().GetGroupsAsync(cancellationToken)
            : Task.CompletedTask;
}
