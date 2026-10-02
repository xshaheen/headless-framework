// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>Lets a module contribute Messaging registrations without owning the host's messaging setup.</summary>
[PublicAPI]
public static class MessagingContributionExtensions
{
    /// <summary>
    /// Contributes Messaging registrations from a module. The contribution may run before or after the host's
    /// <c>AddHeadlessMessaging</c> call; contributions apply in the order they were added when messaging starts.
    /// </summary>
    /// <remarks>
    /// <paramref name="configure"/> runs once, synchronously, during this call. A module calls this from its own
    /// <c>Add{Module}</c> entry point instead of calling <c>AddHeadlessMessaging</c>, which the host owns and calls once.
    /// Every registration is recorded as a descriptor and folded once, when the host first builds its consumer registry
    /// at startup or at an earlier publish. Identical contract declarations merge there; conflicting ones fail naming
    /// both.
    /// </remarks>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configure">Adds this module's registrations.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IServiceCollection ConfigureMessaging(
        this IServiceCollection services,
        [InstantHandle] Action<MessagingContributionBuilder> configure
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        var builder = new MessagingContributionBuilder(services);
        configure(builder);
        builder.Complete();

        return services;
    }
}
