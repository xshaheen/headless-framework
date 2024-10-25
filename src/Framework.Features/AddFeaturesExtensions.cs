// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Checkers;
using Framework.Features.Definitions;
using Framework.Features.Filters;
using Framework.Features.Models;
using Framework.Features.ValueProviders;
using Framework.Features.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Framework.Features;

[PublicAPI]
public static class AddFeaturesExtensions
{
    public static IHostApplicationBuilder AddFrameworkFeatures(this IHostApplicationBuilder builder)
    {
        _AddCoreValueProviders(builder);

        builder.Services.AddSingleton<IFeatureDefinitionManager, FeatureDefinitionManager>();
        builder.Services.AddSingleton<IFeatureValueProviderManager, FeatureValueProviderManager>();

        builder.Services.AddSingleton<IFeatureChecker, FeatureChecker>();
        builder.Services.AddSingleton<IMethodInvocationFeatureCheckerService, MethodInvocationFeatureCheckerService>();

        return builder;
    }

    public static void AddFeatureDefinitionProvider<T>(this IServiceCollection services)
        where T : class, IFeatureDefinitionProvider
    {
        services.AddSingleton<T>();

        services.Configure<FeatureManagementProvidersOptions>(options =>
        {
            options.DefinitionProviders.Add<T>();
        });
    }

    public static void AddFeatureValueProvider<T>(this IServiceCollection services)
        where T : class, IFeatureValueReadProvider
    {
        services.AddSingleton<T>();

        services.Configure<FeatureManagementProvidersOptions>(options =>
        {
            if (!options.ValueProviders.Contains<T>())
            {
                options.ValueProviders.Add<T>();
            }
        });
    }

    private static void _AddCoreValueProviders(IHostApplicationBuilder builder)
    {
        builder.Services.Configure<FeatureManagementProvidersOptions>(options =>
        {
            options.ValueProviders.Add<DefaultValueFeatureValueProvider>();
            options.ValueProviders.Add<EditionFeatureValueProvider>();
            options.ValueProviders.Add<TenantFeatureValueProvider>();
        });

        builder.Services.AddSingleton<DefaultValueFeatureValueProvider>();
        builder.Services.AddSingleton<EditionFeatureValueProvider>();
        builder.Services.AddSingleton<TenantFeatureValueProvider>();
    }
}
