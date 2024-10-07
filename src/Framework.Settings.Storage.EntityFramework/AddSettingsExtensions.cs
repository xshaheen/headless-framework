// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Settings.Definitions;
using Framework.Settings.Helpers;
using Framework.Settings.Repositories;
using Framework.Settings.ValueProviders;
using Framework.Settings.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Framework.Settings;

[PublicAPI]
public static class AddSettingsExtensions
{
    public static IHostApplicationBuilder AddFrameworkSettings(this IHostApplicationBuilder builder)
    {
        builder.Services._AddSettingEncryption();
        builder.Services._AddCoreValueProvider();

        builder.Services.TryAddSingleton<
            IDynamicSettingDefinitionStoreInMemoryCache,
            DynamicSettingDefinitionStoreInMemoryCache
        >();

        builder.Services.TryAddSingleton<IDynamicSettingDefinitionStore, DynamicSettingDefinitionStore>();
        builder.Services.TryAddSingleton<IStaticSettingDefinitionStore, StaticSettingDefinitionStore>();
        builder.Services.TryAddSingleton<ISettingDefinitionManager, SettingDefinitionManager>();
        builder.Services.TryAddSingleton<ISettingValueProviderManager, SettingValueProviderManager>();
        builder.Services.TryAddTransient<ISettingProvider, SettingProvider>();
        // This is a fallback store, it should be replaced by a real store
        builder.Services.TryAddSingleton<ISettingValueStore, NullSettingStore>();

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
        services.AddSingletonOptions<StringEncryptionSettings, StringEncryptionOptionsValidator>();
        services.TryAddSingleton<IStringEncryptionService, StringEncryptionService>();
        services.AddSingleton<ISettingEncryptionService, SettingEncryptionService>();
    }

    private static void _AddCoreValueProvider(this IServiceCollection services)
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

        services.AddSingleton<ISettingValueProvider, DefaultValueSettingValueProvider>();
        services.AddSingleton<ISettingValueProvider, ConfigurationSettingValueProvider>();
        services.AddSingleton<ISettingValueProvider, GlobalSettingValueProvider>();
        services.AddSingleton<ISettingValueProvider, TenantSettingValueProvider>();
    }
    /*
     * public class SettingManagementDomainModule : Module
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task _initializeDynamicSettingsTask;

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<SettingManagementOptions>(options =>
        {
            options.Providers.Add<DefaultValueSettingManagementProvider>();
            options.Providers.Add<ConfigurationSettingManagementProvider>();
            options.Providers.Add<GlobalSettingManagementProvider>();
            options.Providers.Add<TenantSettingManagementProvider>();
            options.Providers.Add<UserSettingManagementProvider>();
        });

        if (context.Services.IsDataMigrationEnvironment())
        {
            Configure<SettingManagementOptions>(options =>
            {
                options.SaveStaticSettingsToDatabase = false;
                options.IsDynamicSettingStoreEnabled = false;
            });
        }
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        AsyncHelper.RunSync(() => OnApplicationInitializationAsync(context));
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        InitializeDynamicSettings(context);
        return Task.CompletedTask;
    }

    public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        _cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }

    private void InitializeDynamicSettings(ApplicationInitializationContext context)
    {
        var options = context.ServiceProvider.GetRequiredService<IOptions<SettingManagementOptions>>().Value;

        if (!options.SaveStaticSettingsToDatabase && !options.IsDynamicSettingStoreEnabled)
        {
            return;
        }

        var rootServiceProvider = context.ServiceProvider.GetRequiredService<IRootServiceProvider>();

        _initializeDynamicSettingsTask = Task.Run(async () =>
        {
            using var scope = rootServiceProvider.CreateScope();
            var applicationLifetime = scope.ServiceProvider.GetService<IHostApplicationLifetime>();
            var cancellationTokenProvider = scope.ServiceProvider.GetRequiredService<ICancellationTokenProvider>();
            var cancellationToken = applicationLifetime?.ApplicationStopping ?? _cancellationTokenSource.Token;

            try
            {
                using (cancellationTokenProvider.Use(cancellationToken))
                {
                    if (cancellationTokenProvider.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    await SaveStaticSettingsToDatabaseAsync(options, scope, cancellationTokenProvider);

                    if (cancellationTokenProvider.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    await PreCacheDynamicSettingsAsync(options, scope);
                }
            }
            // ReSharper disable once EmptyGeneralCatchClause (No need to log since it is logged above)
            catch { }
        });
    }

    private static async Task SaveStaticSettingsToDatabaseAsync(
        SettingManagementOptions options,
        IServiceScope scope,
        ICancellationTokenProvider cancellationTokenProvider
    )
    {
        if (!options.SaveStaticSettingsToDatabase)
        {
            return;
        }

        await Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(8, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt) * 10))
            .ExecuteAsync(
                async _ =>
                {
                    try
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        await scope.ServiceProvider.GetRequiredService<IStaticSettingSaver>().SaveAsync();
                    }
                    catch (Exception ex)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        scope.ServiceProvider.GetService<ILogger<SettingManagementDomainModule>>()?.LogException(ex);

                        throw; // Polly will catch it
                    }
                },
                cancellationTokenProvider.Token
            );
    }

    private static async Task PreCacheDynamicSettingsAsync(SettingManagementOptions options, IServiceScope scope)
    {
        if (!options.IsDynamicSettingStoreEnabled)
        {
            return;
        }

        try
        {
            // Pre-cache settings, so first request doesn't wait
            await scope.ServiceProvider.GetRequiredService<IDynamicSettingDefinitionStore>().GetAllAsync();
        }
        catch (Exception ex)
        {
            // ReSharper disable once AccessToDisposedClosure
            scope.ServiceProvider.GetService<ILogger<SettingManagementDomainModule>>()?.LogException(ex);

            throw; // It will be cached in InitializeDynamicSettings
        }
    }
}

     */
}
