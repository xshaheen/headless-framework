// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>
/// Setup-time extension hook for tenant catalog storage provider packages. Implementations register
/// provider-specific services into the DI container during the <c>Catalog(...)</c> build phase.
/// </summary>
[PublicAPI]
public interface ITenantCatalogStorageOptionsExtension
{
    /// <summary>Registers the services required by this storage provider extension.</summary>
    /// <param name="services">The application service collection to register into.</param>
    void AddServices(IServiceCollection services);
}
