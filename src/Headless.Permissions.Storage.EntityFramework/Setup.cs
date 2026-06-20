// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Permissions;
using Headless.Permissions.Internal;
using Headless.Permissions.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Entity Framework Core storage provider for Headless Permissions.
/// </summary>
[PublicAPI]
public static class SetupPermissions
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        /// <summary>
        /// Configures the permissions system to persist grants and definitions through the consumer's
        /// <typeparamref name="TContext"/> EF Core context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Registers <see cref="EfPermissionGrantRepository{TContext}"/> and
        /// <see cref="EfPermissionDefinitionRecordRepository{TContext}"/> as singletons, both backed by
        /// <c>IDbContextFactory&lt;<typeparamref name="TContext"/>&gt;</c>. Ensure the factory is registered
        /// (e.g., <c>services.AddDbContextFactory&lt;TContext&gt;()</c>).
        /// </para>
        /// <para>
        /// Call <c>modelBuilder.AddHeadlessPermissions(this)</c> inside
        /// <c>OnModelCreating</c> so that <typeparamref name="TContext"/> maps the three permissions
        /// entities; a startup gate validates the mapping before hosted services start and throws
        /// <see cref="InvalidOperationException"/> with an actionable message if any entity is missing.
        /// </para>
        /// <para>
        /// Schema and table-name validation uses the most permissive identifier rules (SQL Server superset)
        /// so that the same options class is usable regardless of the underlying database engine; the actual
        /// database enforces type- and length-specific constraints at migration time.
        /// </para>
        /// </remarks>
        public HeadlessPermissionsSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkPermissionsOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkPermissionsOptionsExtension(Type dbContextType)
        : IPermissionsStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<PermissionsStorageOptions, EntityFrameworkPermissionsStorageOptionsValidator>();
            services.TryAddSingleton(
                typeof(IPermissionGrantRepository),
                typeof(EfPermissionGrantRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(IPermissionDefinitionRecordRepository),
                typeof(EfPermissionDefinitionRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(PermissionsEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }

    // EF dispatches to whatever DB the consumer wired up, so the validator uses the most
    // permissive identifier pattern (SqlServer, a superset of PostgreSQL's character set) and
    // the larger length cap (SqlServer). The underlying DB surfaces type/length issues at
    // migration time.
    private sealed class EntityFrameworkPermissionsStorageOptionsValidator
        : AbstractValidator<PermissionsStorageOptions>
    {
        public EntityFrameworkPermissionsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidCrossProviderIdentifier();
            RuleFor(x => x.PermissionGrantsTableName).IsValidCrossProviderIdentifier();
            RuleFor(x => x.PermissionDefinitionsTableName).IsValidCrossProviderIdentifier();
            RuleFor(x => x.PermissionGroupDefinitionsTableName).IsValidCrossProviderIdentifier();
        }
    }
}
