// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Features;

/// <summary>
/// Writes the code-declared feature definitions into the dynamic store when
/// <see cref="FeatureManagementOptions.SaveStaticFeaturesToDatabase"/> is <see langword="true"/>.
/// </summary>
internal sealed class FeatureDefinitionsSeeder(
    IServiceProvider services,
    IOptions<FeatureManagementOptions> optionsAccessor,
    ILogger<FeatureDefinitionsSeeder> logger,
    TimeProvider retryClock
) : DefinitionStoreSeeder(logger, retryClock)
{
    public override string Name => "FeatureDefinitions";

    protected override bool IsEnabled => optionsAccessor.Value.SaveStaticFeaturesToDatabase;

    protected override Task SaveAsync(CancellationToken cancellationToken) =>
        services.GetRequiredService<IDynamicFeatureDefinitionStore>().SaveAsync(cancellationToken);

    protected override async Task<SeedState> VerifyStoreAsync(CancellationToken cancellationToken)
    {
        // A store the application replaced has no comparison this seeder knows, so it cannot tell.
        if (services.GetRequiredService<IDynamicFeatureDefinitionStore>() is not DynamicFeatureDefinitionStore store)
        {
            return SeedState.Unknown;
        }

        return await store.HoldsStaticDefinitionsAsync(cancellationToken).ConfigureAwait(false)
            ? SeedState.Current
            : SeedState.Stale;
    }
}
