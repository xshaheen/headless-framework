using Framework.Api.Core.Abstractions;
using Framework.Features.Definitions;
using Framework.Features.Values;

namespace Framework.Features.Providers;

[PublicAPI]
public sealed class TenantFeatureValueProvider(IFeatureStore store, ICurrentTenant currentTenant)
    : FeatureValueProvider(store)
{
    public const string ProviderName = "Tenant";

    public override string Name => ProviderName;

    public override Task<string?> GetOrDefaultAsync(FeatureDefinition feature)
    {
        return Store.GetOrDefaultAsync(feature.Name, Name, currentTenant.Id?.ToString());
    }
}
