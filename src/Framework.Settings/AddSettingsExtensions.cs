using Framework.Api.Core.Abstractions;
using Framework.Settings.DefinitionProviders;
using Framework.Settings.DefinitionStores;
using Framework.Settings.Helpers;
using Framework.Settings.ValueProviders;
using Framework.Settings.ValueStores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Framework.Settings;

[PublicAPI]
public static class AddSettingsExtensions
{
    public static IHostApplicationBuilder AddFrameworkSettingsModule(this IHostApplicationBuilder builder)
    {
        builder.Services._AddSettingEncryption();
        builder.Services._AddCoreDefinitionsStore();
        builder.Services._AddCoreSettingValueProvider();

        builder.Services.TryAddTransient<ISettingProvider, SettingProvider>();
        builder.Services.TryAddSingleton<ISettingStore, NullSettingStore>();
        builder.Services.TryAddSingleton<ISettingValueProviderManager, SettingValueProviderManager>();

        return builder;
    }

    public static void AddSettingDefinitionProvider<T>(this IServiceCollection services)
        where T : class, ISettingDefinitionProvider
    {
        services.AddSingleton<ISettingDefinitionProvider, T>();

        services.Configure<FrameworkSettingOptions>(options =>
        {
            options.DefinitionProviders.Add<T>();
        });
    }

    public static void AddSettingValueProvider<T>(this IServiceCollection services)
        where T : class, ISettingValueProvider
    {
        services.AddSingleton<ISettingValueProvider, T>();

        services.Configure<FrameworkSettingOptions>(options =>
        {
            if (!options.ValueProviders.Contains<T>())
            {
                options.ValueProviders.Add<T>();
            }
        });
    }

    private static void _AddSettingEncryption(this IServiceCollection services)
    {
        services.AddOptions<StringEncryptionSettings, StringEncryptionOptionsValidator>();
        services.TryAddSingleton<IStringEncryptionService, StringEncryptionService>();
        services.AddSingleton<ISettingEncryptionService, SettingEncryptionService>();
    }

    private static void _AddCoreSettingValueProvider(this IServiceCollection services)
    {
        services.Configure<FrameworkSettingOptions>(options =>
        {
            // Last added provider has the highest priority
            options.ValueProviders.Add<DefaultValueSettingValueProvider>();
            options.ValueProviders.Add<ConfigurationSettingValueProvider>();
            options.ValueProviders.Add<GlobalSettingValueProvider>();
            options.ValueProviders.Add<TenantSettingValueProvider>();
            options.ValueProviders.Add<UserSettingValueProvider>();
        });
    }

    private static void _AddCoreDefinitionsStore(this IServiceCollection services)
    {
        services.AddSingleton<IStaticSettingDefinitionStore, StaticSettingDefinitionStore>();
        services.AddSingleton<IDynamicSettingDefinitionStore, NullDynamicSettingDefinitionStore>();
        services.AddSingleton<ISettingDefinitionManager, SettingDefinitionManager>();
    }
}
