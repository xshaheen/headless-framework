// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Http.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Headless.Sms;

/// <summary>
/// Provides shared registration helpers for HTTP-backed SMS providers.
/// </summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HttpSmsProviderRegistration
{
    /// <summary>
    /// Gets the per-instance HTTP client name used for client registration and resilience pipeline lookup.
    /// </summary>
    /// <param name="httpClientName">The base HTTP client name of the provider.</param>
    /// <param name="name">The named instance identifier, or <see langword="null"/> for the default sender.</param>
    /// <returns>The computed HTTP client name.</returns>
    public static string GetHttpClientName(string httpClientName, string? name)
    {
        return name is null ? httpClientName : $"{httpClientName}:{name}";
    }

    /// <summary>
    /// Registers an HTTP-backed SMS sender with options, resilience handlers, and dependency injection services.
    /// </summary>
    /// <typeparam name="TSender">The concrete sender type implementing <see cref="ISmsSender"/>.</typeparam>
    /// <param name="services">The service collection instance.</param>
    /// <param name="name">The named instance identifier, or <see langword="null"/> for the default sender.</param>
    /// <param name="httpClientName">The base HTTP client name.</param>
    /// <param name="configureOptions">A delegate that registers and validates options for the specified name.</param>
    /// <param name="createSender">A factory delegate that creates the sender instance.</param>
    /// <param name="effect">The side-effect classification determining resilience policy.</param>
    /// <param name="configureClient">An optional delegate to configure the underlying HTTP client.</param>
    /// <param name="configureResilience">An optional delegate to customize resilience settings.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="configureOptions"/>, or <paramref name="createSender"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="httpClientName"/> is empty.</exception>
    public static void AddHttpSmsProvider<TSender>(
        IServiceCollection services,
        string? name,
        string httpClientName,
        Action<IServiceCollection, string?> configureOptions,
        Func<IServiceProvider, string, string?, TSender> createSender,
        OutboundEffect effect,
        Action<HttpClient>? configureClient,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
        where TSender : class, ISmsSender
    {
        Argument.IsNotNull(services);
        Argument.IsNotNullOrEmpty(httpClientName);
        Argument.IsNotNull(configureOptions);
        Argument.IsNotNull(createSender);

        configureOptions(services, name);

        var instanceClientName = GetHttpClientName(httpClientName, name);

        var httpClientBuilder = configureClient is null
            ? services.AddHttpClient(instanceClientName)
            : services.AddHttpClient(instanceClientName, configureClient);

        // Derive resilience handler from the provider effect and remove host-wide outer wrappers.
        httpClientBuilder.AddEffectResilienceHandler(effect, configureResilience: configureResilience);

        var registersBulkForward = typeof(IBulkSmsSender).IsAssignableFrom(typeof(TSender));

        if (name is null)
        {
            services.AddSingleton<ISmsSender>(sp => createSender(sp, instanceClientName, null));

            if (registersBulkForward)
            {
                services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());
            }

            return;
        }

        services.AddKeyedSingleton<ISmsSender>(name, (sp, _) => createSender(sp, instanceClientName, name));

        if (registersBulkForward)
        {
            services.AddKeyedSingleton<IBulkSmsSender>(
                name,
                (sp, _) => (IBulkSmsSender)sp.GetRequiredKeyedService<ISmsSender>(name)
            );
        }
    }
}
