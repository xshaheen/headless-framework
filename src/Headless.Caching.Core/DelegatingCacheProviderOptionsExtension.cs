// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching;

/// <summary>
/// A lightweight <see cref="ICacheProviderOptionsExtension"/> that delegates service registration to an
/// <see cref="Action{IServiceCollection}"/> supplied at construction time. Provider Setup classes use this
/// instead of declaring their own identical private wrapper type.
/// </summary>
[PublicAPI]
public sealed class DelegatingCacheProviderOptionsExtension(Action<IServiceCollection> apply)
    : ICacheProviderOptionsExtension
{
    public void AddServices(IServiceCollection services) => apply(services);
}
