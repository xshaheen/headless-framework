// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering message consumers directly on <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers message-level metadata and zero or more consumers for <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The message type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The message registration callback.</param>
    /// <returns>The current <see cref="IServiceCollection"/> instance.</returns>
    [PublicAPI]
    public static IServiceCollection ForMessage<TMessage>(
        this IServiceCollection services,
        Action<IMessageBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(configure);

        var builder = new MessageBuilder<TMessage>(services);
        configure(builder);
        services.AddSingleton(builder.Build());

        return services;
    }
}
