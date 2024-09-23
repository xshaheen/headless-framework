// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Definitions;
using Framework.Features.Values;

namespace Framework.Features.Providers;

[PublicAPI]
public sealed class DefaultValueFeatureValueProvider : IFeatureValueProvider
{
    public const string ProviderName = "DefaultValue";

    public string Name => ProviderName;

    public Task<string?> GetOrDefaultAsync(FeatureDefinition feature)
    {
        return Task.FromResult(feature.DefaultValue);
    }
}
