// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;

namespace Framework.Features.Values;

public interface IFeatureValueProvider
{
    string Name { get; }

    Task<string?> GetOrDefaultAsync(FeatureDefinition feature);
}

public abstract class FeatureValueProvider(IFeatureStore store) : IFeatureValueProvider
{
    public abstract string Name { get; }

    protected IFeatureStore Store { get; } = store;

    public abstract Task<string?> GetOrDefaultAsync(FeatureDefinition feature);
}
