// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Permissions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Permissions;

/// <summary>
/// Registers the Entity Framework Core storage provider for Headless Permissions.
/// </summary>
[PublicAPI]
public static class SetupPermissionsEntityFramework
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        /// <summary>
        /// Configures the permissions system to persist grants and definitions through the consumer's
        /// <typeparamref name="TContext"/> EF Core context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Registers the <see cref="IPermissionGrantRepository"/> and
        /// <see cref="IPermissionDefinitionRecordRepository"/> implementations as singletons, both backed by
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
            services.RequireSingletonService(
                typeof(IDbContextFactory<>).MakeGenericType(dbContextType),
                requiredBy: "Headless permissions EF storage",
                remedy: "Register it at the default singleton lifetime: AddDbContextFactory<TContext>() or AddPooledDbContextFactory<TContext>() for a plain DbContext, or AddHeadlessDbContext<TContext>() or AddHeadlessDbContextPool<TContext>() for a HeadlessDbContext. The store is a singleton and would keep one scoped or transient factory for the life of the host."
            );
            services.AddStartupValidator(typeof(PermissionsEntityStartupValidator<>).MakeGenericType(dbContextType));
        }
    }

    // EF dispatches to whatever DB the consumer wired up, so the validator uses the most
    // permissive identifier pattern (SqlServer, a superset of PostgreSQL's character set) and
    // the larger length cap (SqlServer). The underlying DB surfaces type/length issues at
    // migration time, except derived key and index names, which PostgreSQL truncates instead
    // of rejecting, so those are bounded here.
    private sealed class EntityFrameworkPermissionsStorageOptionsValidator
        : AbstractValidator<PermissionsStorageOptions>
    {
        public EntityFrameworkPermissionsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidCrossProviderIdentifier();
            RuleFor(x => x.PermissionGrantsTableName)
                .IsValidCrossProviderIdentifier()
                .FitsDerivedPostgreSqlNames(PermissionsStorageNames.GrantsIndexes)
                .When(x => x.PermissionGrantsTableName is not null);
            RuleFor(x => x.PermissionDefinitionsTableName)
                .IsValidCrossProviderIdentifier()
                .FitsDerivedPostgreSqlNames(PermissionsStorageNames.DefinitionsIndexes)
                .When(x => x.PermissionDefinitionsTableName is not null);
            RuleFor(x => x.PermissionGroupDefinitionsTableName)
                .IsValidCrossProviderIdentifier()
                .FitsDerivedPostgreSqlNames(PermissionsStorageNames.GroupsIndexes)
                .When(x => x.PermissionGroupDefinitionsTableName is not null);
        }
    }
}
