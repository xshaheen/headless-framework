// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Caching;

/// <summary>Setup-time extension hook for cache provider packages.</summary>
[PublicAPI]
public interface ICacheProviderOptionsExtension
{
    void AddServices(IServiceCollection services);
}
