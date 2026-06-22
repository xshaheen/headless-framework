// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features;

/// <summary>Setup-time extension hook for features storage provider packages.</summary>
[PublicAPI]
public interface IFeaturesStorageOptionsExtension
{
    /// <summary>Registers the provider-specific services into <paramref name="services"/>.</summary>
    /// <param name="services">The application service collection to populate.</param>
    void AddServices(IServiceCollection services);
}
