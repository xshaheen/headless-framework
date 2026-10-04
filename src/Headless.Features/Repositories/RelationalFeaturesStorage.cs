// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Headless.Hosting.Initialization.Schema;
using Headless.Serializer;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Features;

/// <summary>Registers the relational repositories and a provider's schema contribution over one dialect.</summary>
internal static class RelationalFeaturesStorage
{
    public static void AddServices<TOptions>(
        IServiceCollection services,
        ISqlDialect dialect,
        StorageProvider provider,
        Func<TOptions, RelationalFeaturesTables, bool, SchemaContribution> createContribution
    )
        where TOptions : RelationalFeaturesOptions
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidator<FeaturesStorageOptions>>(
                new RelationalFeaturesStorageOptionsValidator(provider)
            )
        );
        services.AddOptions<FeaturesStorageOptions>().ValidateFluentValidation().ValidateOnStart();
        services.TryAddSingleton(sp => new RelationalFeaturesTables(
            dialect,
            sp.GetRequiredService<IOptions<FeaturesStorageOptions>>().Value
        ));

        // The contribution factory reads options at first resolution, so InitializeOnStartup keeps its contract:
        // false keeps the steps out of the runner's apply pass while verify and export still see them.
        services.AddHeadlessSchemaContribution(sp =>
            createContribution(
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<RelationalFeaturesTables>(),
                sp.GetRequiredService<IOptions<FeaturesStorageOptions>>().Value.InitializeOnStartup
            )
        );

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
        services.TryAddSingleton<IFeatureValueRecordRepository>(sp => new RelationalFeatureValueRecordRepository(
            sp.GetRequiredService<RelationalFeaturesTables>(),
            sp.GetRequiredService<IOptions<TOptions>>().Value,
            sp.GetRequiredService<TimeProvider>()
        ));
        services.TryAddSingleton<IFeatureDefinitionRecordRepository>(
            sp => new RelationalFeatureDefinitionRecordRepository(
                sp.GetRequiredService<RelationalFeaturesTables>(),
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<IJsonSerializer>()
            )
        );
    }
}
