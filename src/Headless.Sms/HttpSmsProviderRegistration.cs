// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Http.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Headless.Sms;

/// <summary>
/// The shared registration path for HTTP-backed SMS providers: options, a per-instance named HttpClient whose
/// resilience pipeline is derived from the provider's declared <see cref="OutboundEffect"/>, and the default or
/// keyed sender (plus the bulk-sender forward when the sender supports bulk sends).
/// </summary>
/// <remarks>
/// Provider packages call this from their <c>Use{Provider}</c> members; it is plumbing, not an application API.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)] // provider-package plumbing, not an application-code API
public static class HttpSmsProviderRegistration
{
    /// <summary>
    /// The per-instance client name. It doubles as the resilience-pipeline key, so each named instance gets its own
    /// client registration and pipeline.
    /// </summary>
    /// <param name="httpClientName">The provider's base client name, for example <c>Headless:InfobipSms</c>.</param>
    /// <param name="name">The named instance, or <see langword="null"/> for the default sender.</param>
    /// <returns>The base name for the default sender; <c>{httpClientName}:{name}</c> for a named instance.</returns>
    public static string GetHttpClientName(string httpClientName, string? name)
    {
        return name is null ? httpClientName : $"{httpClientName}:{name}";
    }

    /// <summary>
    /// Registers one HTTP-backed SMS sender. A <see langword="null"/> <paramref name="name"/> registers the default
    /// (unkeyed) sender; a non-null name registers a keyed sender, named options, and a per-name HttpClient.
    /// </summary>
    /// <remarks>
    /// <paramref name="createSender"/> receives the per-instance client name and the options name, so every sender
    /// reads the options snapshot for its own name (<c>IOptionsMonitor.Get(name)</c>): keyed DI does not cascade the
    /// key to constructor dependencies, and a keyed sender must not read <c>CurrentValue</c>, which binds the default.
    /// </remarks>
    /// <typeparam name="TSender">The provider's sender; a bulk-sender forward is registered when it implements <see cref="IBulkSmsSender"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The named instance, or <see langword="null"/> for the default sender.</param>
    /// <param name="httpClientName">The provider's base client name.</param>
    /// <param name="configureOptions">Registers and validates the provider options under the given name.</param>
    /// <param name="createSender">Creates the sender from the provider, the per-instance client name, and the options name.</param>
    /// <param name="effect">The provider's declared side-effect class, from which the resilience pipeline is derived.</param>
    /// <param name="configureClient">Optional consumer configuration of the HttpClient.</param>
    /// <param name="configureResilience">Optional consumer tuning, applied after the derived resilience defaults.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
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

        // The pipeline comes from the provider's declared effect, which also removes any host-wide handler
        // (such as service defaults) that would otherwise stack as an outer pipeline and retry anyway.
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
