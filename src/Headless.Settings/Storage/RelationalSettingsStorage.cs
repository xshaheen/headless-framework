// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting.Initialization.Schema;
using Headless.Serializer;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Settings;

/// <summary>Registers the relational repositories and a provider's schema contribution over one dialect.</summary>
internal static class RelationalSettingsStorage
{
    public static void AddServices<TOptions>(
        IServiceCollection services,
        ISqlDialect dialect,
        StorageProvider provider,
        Func<TOptions, RelationalSettingsTables, bool, SchemaContribution> createContribution
    )
        where TOptions : RelationalSettingsOptions
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidator<SettingsStorageOptions>>(
                new RelationalSettingsStorageOptionsValidator(provider)
            )
        );
        services.AddOptions<SettingsStorageOptions>().ValidateFluentValidation().ValidateOnStart();
        services.TryAddSingleton(sp => new RelationalSettingsTables(
            dialect,
            sp.GetRequiredService<IOptions<SettingsStorageOptions>>().Value
        ));

        // The contribution factory reads options at first resolution, so InitializeOnStartup keeps its contract:
        // false keeps the steps out of the runner's apply pass while verify and export still see them.
        services.AddHeadlessSchemaContribution(sp =>
            createContribution(
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<RelationalSettingsTables>(),
                sp.GetRequiredService<IOptions<SettingsStorageOptions>>().Value.InitializeOnStartup
            )
        );

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
        services.TryAddSingleton<ISettingValueRecordRepository>(sp => new RelationalSettingValueRecordRepository(
            sp.GetRequiredService<RelationalSettingsTables>(),
            sp.GetRequiredService<IOptions<TOptions>>().Value,
            sp.GetRequiredService<TimeProvider>()
        ));
        services.TryAddSingleton<ISettingDefinitionRecordRepository>(
            sp => new RelationalSettingDefinitionRecordRepository(
                sp.GetRequiredService<RelationalSettingsTables>(),
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<IJsonSerializer>()
            )
        );
    }
}
