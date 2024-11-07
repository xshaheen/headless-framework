// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;
using Framework.Features.Values;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.BuildingBlocks.Helpers.Threading;

namespace Framework.Features.ValueProviders;

[PublicAPI]
public sealed class TenantFeatureValueProvider(IFeatureValueStore store, ICurrentTenant currentTenant)
    : StoreFeatureValueProvider(store)
{
    public const string ProviderName = "Tenant";

    public override string Name => ProviderName;

    public override Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.Equals(providerName, Name, StringComparison.Ordinal) || providerKey.IsNullOrWhiteSpace())
        {
            return base.HandleContextAsync(providerName, providerKey, cancellationToken);
        }

        var disposable = currentTenant.Change(providerKey);

        var asyncDisposable = new AsyncDisposeFunc(() =>
        {
            disposable.Dispose();

            return Task.CompletedTask;
        });

        return Task.FromResult<IAsyncDisposable>(asyncDisposable);
    }

    public override Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return Store.GetOrDefaultAsync(feature.Name, Name, currentTenant.Id, cancellationToken);
    }
}
