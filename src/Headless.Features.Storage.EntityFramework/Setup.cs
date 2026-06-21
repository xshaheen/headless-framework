// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Features;
using Headless.Features.Internal;
using Headless.Features.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the Entity Framework Core storage provider for Headless Features.</summary>
[PublicAPI]
public static class SetupFeaturesEntityFramework
{
    extension(HeadlessFeaturesSetupBuilder setup)
    {
        /// <summary>
        /// Configures Headless Features to persist feature definitions and values via Entity Framework Core
        /// using <typeparamref name="TContext"/> as the backing <see cref="DbContext"/>.
        /// </summary>
        /// <typeparam name="TContext">
        /// The <see cref="DbContext"/> type whose model must include the Headless feature entities
        /// (registered by calling <c>modelBuilder.AddHeadlessFeatures(...)</c> in <c>OnModelCreating</c>).
        /// </typeparam>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance for chaining.</returns>
        public HeadlessFeaturesSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkFeaturesOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    /// <summary>Wires EF Core repository and startup-gate services for a specific <see cref="DbContext"/> type.</summary>
    private sealed class EntityFrameworkFeaturesOptionsExtension(Type dbContextType) : IFeaturesStorageOptionsExtension
    {
        /// <inheritdoc/>
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<FeaturesStorageOptions, EntityFrameworkFeaturesStorageOptionsValidator>();
            services.TryAddSingleton(
                typeof(IFeatureValueRecordRepository),
                typeof(EfFeatureValueRecordRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(IFeatureDefinitionRecordRepository),
                typeof(EfFeatureDefinitionRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(FeaturesEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }

    // EF dispatches to whatever DB the consumer wired up, so the validator uses the most
    // permissive identifier pattern (SqlServer, a superset of PostgreSQL's character set) and
    // the larger length cap (SqlServer). The underlying DB surfaces type/length issues at
    // migration time.
    /// <summary>
    /// Validates <see cref="FeaturesStorageOptions"/> using cross-provider identifier rules
    /// (SQL Server superset) so the same validation works for every EF-backed database.
    /// </summary>
    private sealed class EntityFrameworkFeaturesStorageOptionsValidator : AbstractValidator<FeaturesStorageOptions>
    {
        /// <summary>Initializes validation rules for all table-name and schema properties.</summary>
        public EntityFrameworkFeaturesStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidCrossProviderIdentifier();
            RuleFor(x => x.FeatureValuesTableName).IsValidCrossProviderIdentifier();
            RuleFor(x => x.FeatureDefinitionsTableName).IsValidCrossProviderIdentifier();
            RuleFor(x => x.FeatureGroupDefinitionsTableName).IsValidCrossProviderIdentifier();
        }
    }
}
