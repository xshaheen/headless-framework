// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Features;
using Headless.Features.Internal;
using Headless.Features.Repositories;
using Headless.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupFeaturesEntityFramework
{
    extension(HeadlessFeaturesSetupBuilder setup)
    {
        public HeadlessFeaturesSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkFeaturesOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkFeaturesOptionsExtension(Type dbContextType) : IFeaturesStorageOptionsExtension
    {
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

    // EF dispatches to whatever DB the consumer wired up, so the validator caps at the most
    // permissive limit (SqlServerMaxLength). PG-via-EF consumers with longer identifiers will
    // surface a clearer error from the EF migration than the validator could.
    private sealed class EntityFrameworkFeaturesStorageOptionsValidator : AbstractValidator<FeaturesStorageOptions>
    {
        public EntityFrameworkFeaturesStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.FeatureValuesTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.FeatureDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.FeatureGroupDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        }
    }
}
