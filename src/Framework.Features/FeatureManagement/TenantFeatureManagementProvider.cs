// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Providers;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.BuildingBlocks.Helpers.Threading;

namespace Framework.Features.FeatureManagement;

public class TenantFeatureManagementProvider : FeatureManagementProvider
{
    public override string Name => TenantFeatureValueProvider.ProviderName;

    protected ICurrentTenant CurrentTenant { get; }

    public TenantFeatureManagementProvider(IFeatureManagementStore store, ICurrentTenant currentTenant)
        : base(store)
    {
        CurrentTenant = currentTenant;
    }

    public override Task<IAsyncDisposable> HandleContextAsync(string providerName, string providerKey)
    {
        if (providerName == Name && !providerKey.IsNullOrWhiteSpace())
        {
            if (Guid.TryParse(providerKey, out var tenantId))
            {
                var disposable = CurrentTenant.Change(tenantId);
                return Task.FromResult<IAsyncDisposable>(
                    new AsyncDisposeFunc(() =>
                    {
                        disposable.Dispose();
                        return Task.CompletedTask;
                    })
                );
            }
        }

        return base.HandleContextAsync(providerName, providerKey);
    }

    protected override Task<string?> NormalizeProviderKeyAsync(string? providerKey)
    {
        if (providerKey != null)
        {
            return Task.FromResult<string?>(providerKey);
        }

        return Task.FromResult(CurrentTenant.Id?.ToString());
    }
}
