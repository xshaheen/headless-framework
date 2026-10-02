// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

/// <summary>
/// Lets a library contribute its generated consumers while depending only on the messaging abstractions.
/// </summary>
[PublicAPI]
public static class MessagingModuleRegistrationExtensions
{
    /// <summary>
    /// Contributes one assembly's generated <see cref="IMessagingModule"/>, for example
    /// <c>AddMessagingModule&lt;Billing.MessagingModule&gt;()</c>. It makes the same registration as <c>AddModule</c> in a
    /// <c>ConfigureMessaging</c> contribution, available to a library that does not reference
    /// <c>Headless.Messaging.Core</c>.
    /// </summary>
    /// <remarks>
    /// The call only records the module, so it may come before or after the host's <c>AddHeadlessMessaging</c>, and it
    /// stays inert in a host without messaging. Messaging registers the module's consumers once, when the host first
    /// builds its consumer registry; adding the same module more than once registers it once.
    /// </remarks>
    /// <typeparam name="TModule">The generated module of the assembly that declares the consumers.</typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddMessagingModule<TModule>(this IServiceCollection services)
        where TModule : IMessagingModule
    {
        Argument.IsNotNull(services);

        services.AddSingleton(
            new MessagingModuleContribution(typeof(TModule), static catalog => TModule.Register(catalog))
        );

        return services;
    }
}

/// <summary>One generated module that a contribution or the host asked messaging to register.</summary>
/// <param name="ModuleType">The generated module type, which identifies the module across contributions.</param>
/// <param name="Register">Runs the module's generated registration against a catalog.</param>
internal sealed record MessagingModuleContribution(Type ModuleType, Action<MessagingCatalogBuilder> Register);
