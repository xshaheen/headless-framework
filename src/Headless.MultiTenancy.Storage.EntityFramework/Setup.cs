// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.MultiTenancy;

/// <summary>
/// Provides the <c>UseEntityFramework</c> extension member on <see cref="HeadlessTenancyCatalogSetupBuilder"/>
/// that wires EF Core as the tenant catalog's storage backend.
/// </summary>
[PublicAPI]
public static class SetupTenantCatalogEntityFramework
{
    extension(HeadlessTenancyCatalogSetupBuilder setup)
    {
        /// <summary>
        /// Configures EF Core as the tenant catalog storage backend, using <typeparamref name="TContext"/>
        /// as the database context.
        /// </summary>
        /// <typeparam name="TContext">
        /// The <see cref="DbContext"/> type that has been configured with
        /// <c>modelBuilder.AddHeadlessTenancyCatalog(this)</c> in its <c>OnModelCreating</c> override,
        /// which is validated at application startup.
        /// </typeparam>
        /// <returns>The same <see cref="HeadlessTenancyCatalogSetupBuilder"/> to allow chaining.</returns>
        /// <remarks>
        /// A startup gate validates that the registered <typeparamref name="TContext"/> fully configured
        /// <see cref="TenantRecord"/> through <c>modelBuilder.AddHeadlessTenancyCatalog(this)</c> and throws
        /// <see cref="InvalidOperationException"/> when the model did not run that configuration.
        /// </remarks>
        public HeadlessTenancyCatalogSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkTenantCatalogStoreOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    /// <summary>
    /// <see cref="ITenantCatalogStorageOptionsExtension"/> that registers the EF Core-backed
    /// <see cref="ITenantStore"/>/<see cref="ITenantDirectory"/> implementation for a given
    /// <see cref="DbContext"/> type.
    /// </summary>
    /// <param name="dbContextType">The concrete <see cref="DbContext"/> CLR type to use.</param>
    private sealed class EntityFrameworkTenantCatalogStoreOptionsExtension(Type dbContextType)
        : ITenantCatalogStorageOptionsExtension
    {
        /// <inheritdoc/>
        public void AddServices(IServiceCollection services)
        {
            var storeType = typeof(EfTenantStore<>).MakeGenericType(dbContextType);

            // Singleton, matching Headless.Settings.Storage.EntityFramework's EfSettingValueRecordRepository:
            // the store only wraps a thread-safe IDbContextFactory<TContext>, never a scoped DbContext
            // instance directly, so it needs no per-request lifetime. One shared instance is exposed under
            // both service types (mirroring InMemoryTenantStore/ConfigurationTenantStore) so ITenantStore
            // and ITenantDirectory resolve the same object rather than two independently constructed stores.
            services.TryAddSingleton(storeType);
            services.TryAddSingleton(typeof(ITenantStore), sp => sp.GetRequiredService(storeType));
            services.TryAddSingleton(typeof(ITenantDirectory), sp => sp.GetRequiredService(storeType));
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(TenantCatalogEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }
}
