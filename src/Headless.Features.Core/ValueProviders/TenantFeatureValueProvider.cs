// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Features.Models;
using Headless.Features.Values;

namespace Headless.Features.ValueProviders;

[PublicAPI]
public sealed class TenantFeatureValueProvider(IFeatureValueStore store, ICurrentTenant currentTenant)
    : StoreFeatureValueProvider(store)
{
    public const string ProviderName = FeatureValueProviderNames.Tenant;

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

        var asyncDisposable = DisposableFactory.Create(() =>
        {
            disposable.Dispose();

            return ValueTask.CompletedTask;
        });

        return Task.FromResult(asyncDisposable);
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
