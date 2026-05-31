// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>
    /// Scans the specified assembly for closed <see cref="IConsume{TMessage}"/> implementations and registers them as bus consumers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The current <see cref="IServiceCollection"/> instance.</returns>
    [PublicAPI]
    public static IServiceCollection ForMessagesFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(assembly);

        foreach (var (consumerType, messageType) in _FindConsumers(assembly))
        {
            services.TryAdd(new ServiceDescriptor(consumerType, consumerType, ServiceLifetime.Scoped));

            var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
            services.TryAdd(
                new ServiceDescriptor(serviceType, sp => sp.GetRequiredService(consumerType), ServiceLifetime.Scoped)
            );

            services.AddSingleton(MessageRegistrationFactory.CreateScanned(messageType, consumerType));
        }

        return services;
    }

    /// <summary>
    /// Scans the assembly containing <typeparamref name="TMarker"/> for closed <see cref="IConsume{TMessage}"/> implementations.
    /// </summary>
    /// <typeparam name="TMarker">A marker type from the target assembly.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The current <see cref="IServiceCollection"/> instance.</returns>
    [PublicAPI]
    public static IServiceCollection ForMessagesFromAssemblyContaining<TMarker>(this IServiceCollection services) =>
        services.ForMessagesFromAssembly(typeof(TMarker).Assembly);

    internal static IEnumerable<(Type ConsumerType, Type MessageType)> FindConsumers(Assembly assembly) =>
        _FindConsumers(assembly);

    private static IEnumerable<(Type ConsumerType, Type MessageType)> _FindConsumers(Assembly assembly)
    {
        return assembly
            .GetTypes()
            .Where(static t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .SelectMany(static consumerType =>
                consumerType
                    .GetInterfaces()
                    .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
                    .Select(i => (ConsumerType: consumerType, MessageType: i.GetGenericArguments()[0]))
            );
    }
}
