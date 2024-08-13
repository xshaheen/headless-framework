using Framework.Settings.DefinitionProviders;
using Framework.Settings.DefinitionStores;
using Framework.Settings.ValueProviders;
using Framework.Settings.ValueStores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Settings;

[PublicAPI]
public static class AddSettingsExtensions
{
    public static IHostApplicationBuilder AddFrameworkSettingsModule(this IHostApplicationBuilder builder)
    {
        builder.Services._AddCoreDefinitionsStore();
        builder.Services._AddCoreSettingValueProvider();

        // builder.Services.AddTransient<ISettingEncryptionService, SettingEncryptionService>();
        builder.Services.AddTransient<ISettingProvider, SettingProvider>();
        builder.Services.AddSingleton<ISettingStore, NullSettingStore>();
        builder.Services.AddSingleton<ISettingValueProviderManager, SettingValueProviderManager>();

        return builder;
    }

    public static void AddSettingDefinitionProvider<T>(this IServiceCollection services)
        where T : class, ISettingDefinitionProvider
    {
        services.AddTransient<ISettingDefinitionProvider, T>();

        services.Configure<FrameworkSettingOptions>(options =>
        {
            options.DefinitionProviders.Add<T>();
        });
    }

    public static void AddSettingValueProvider<T>(this IServiceCollection services)
        where T : class, ISettingValueProvider, ISettingDefinitionProvider
    {
        services.AddTransient<ISettingDefinitionProvider, T>();

        services.Configure<FrameworkSettingOptions>(options =>
        {
            options.ValueProviders.Add<T>();
        });
    }

    private static void _AddCoreSettingValueProvider(this IServiceCollection services)
    {
        services.Configure<FrameworkSettingOptions>(options =>
        {
            // Last added provider has the highest priority
            options.ValueProviders.Add<DefaultValueSettingValueProvider>();
            options.ValueProviders.Add<ConfigurationSettingValueProvider>();
            options.ValueProviders.Add<GlobalSettingValueProvider>();
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
