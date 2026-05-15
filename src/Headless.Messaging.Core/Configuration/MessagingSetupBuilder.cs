// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Configuration;

/// <summary>
/// Setup-time builder passed to the <c>AddHeadlessMessaging</c> delegate.
/// Carries the <see cref="MessagingOptions"/> being configured plus the setup-only
/// state (<see cref="IServiceCollection"/>, the consumer registry, the circuit-breaker
/// registry, and the options-extension list) that must not leak into the runtime
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> instance.
/// </summary>
/// <remarks>
/// Splitting these concerns prevents the <c>CopyTo</c> trap where adding a new
/// <see cref="MessagingOptions"/> property silently drops out of the DI-resolved
/// instance if the maintainer forgets to update <c>CopyTo</c>.
/// </remarks>
[PublicAPI]
public sealed class MessagingSetupBuilder : IMessagingBuilder
{
    internal MessagingSetupBuilder(IServiceCollection services, MessagingOptions options, ConsumerRegistry registry)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(options);
        Argument.IsNotNull(registry);

        Services = services;
        Options = options;
        Registry = registry;
    }

    /// <summary>
    /// Gets the runtime <see cref="MessagingOptions"/> being configured.
    /// Mutate this to set value-typed configuration (intervals, batch sizes, JSON serializer
    /// options, retry policy, circuit breaker, etc.).
    /// </summary>
    public MessagingOptions Options { get; }

    internal IServiceCollection Services { get; }

    internal ConsumerRegistry Registry { get; }

    internal ConsumerCircuitBreakerRegistry CircuitBreakerRegistry { get; } = new();

    internal IList<IMessagesOptionsExtension> Extensions { get; } = new List<IMessagesOptionsExtension>();

    /// <summary>
    /// Registers a messaging options extension executed when configuring messaging services.
    /// Extensions allow third-party libraries to customize messaging behavior without modifying core configuration.
    /// </summary>
    /// <param name="extension">The extension instance to register.</param>
    public void RegisterExtension(IMessagesOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }

    /// <inheritdoc />
    public IMessagingBuilder SubscribeFromAssembly(Assembly assembly)
    {
        Argument.IsNotNull(assembly);

        var consumerTypesWithInterfaces = assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Select(t =>
            {
                var interfaces = t.GetInterfaces();
                var consumeInterfaces = interfaces
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
                    .ToList();

                return new { Type = t, ConsumeInterfaces = consumeInterfaces };
            })
            .Where(x => x.ConsumeInterfaces.Count > 0)
            .ToList();

        foreach (var consumer in consumerTypesWithInterfaces)
        {
            foreach (var consumeInterface in consumer.ConsumeInterfaces)
            {
                var messageType = consumeInterface.GetGenericArguments()[0];
                RegisterConsumer(consumer.Type, messageType, topic: null, group: null, concurrency: 1);
            }
        }

        return this;
    }

    /// <inheritdoc />
    public IMessagingBuilder SubscribeFromAssemblyContaining<TMarker>() =>
        SubscribeFromAssembly(typeof(TMarker).Assembly);

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Subscribe<TConsumer>()
        where TConsumer : class
    {
        var messageType = _ResolveExplicitMessageType(typeof(TConsumer));
        var metadata = RegisterConsumer(typeof(TConsumer), messageType, topic: null, group: null, concurrency: 1);

        return new ConsumerBuilder<TConsumer>(
            Options,
            Registry,
            CircuitBreakerRegistry,
            metadata,
            autoRegistered: true
        );
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Subscribe<TConsumer>(string topic)
        where TConsumer : class
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        var messageType = _ResolveExplicitMessageType(typeof(TConsumer));
        Options.WithTopicMapping(messageType, topic);
        var metadata = RegisterConsumer(typeof(TConsumer), messageType, topic, group: null, concurrency: 1);

        return new ConsumerBuilder<TConsumer>(
            Options,
            Registry,
            CircuitBreakerRegistry,
            metadata,
            topic,
            autoRegistered: true
        );
    }

    /// <inheritdoc />
    public IMessagingBuilder Subscribe<TConsumer>(Action<IConsumerBuilder<TConsumer>> configure)
        where TConsumer : class
    {
        Argument.IsNotNull(configure);

        configure(Subscribe<TConsumer>());
        return this;
    }

    /// <inheritdoc />
    public IMessagingBuilder WithTopicMapping<TMessage>(string topic)
        where TMessage : class
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        Options.WithTopicMapping(typeof(TMessage), topic);
        return this;
    }

    /// <inheritdoc />
    public IMessagingBuilder UseConventions(Action<MessagingConventions> configure)
    {
        Argument.IsNotNull(configure);

        configure(Options.Conventions);
        Options.Version = Options.Conventions.Version;
        return this;
    }

    internal ConsumerMetadata RegisterConsumer(
        Type consumerType,
        Type messageType,
        string? topic,
        string? group,
        byte concurrency
    )
    {
        var metadata = Options.CreateConsumerMetadata(consumerType, messageType, topic, group, concurrency);

        Registry.Register(metadata);
        Services.TryAdd(new ServiceDescriptor(consumerType, consumerType, ServiceLifetime.Scoped));

        var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
        Services.TryAdd(
            new ServiceDescriptor(serviceType, sp => sp.GetRequiredService(consumerType), ServiceLifetime.Scoped)
        );

        return metadata;
    }

    private static Type _ResolveExplicitMessageType(Type consumerType)
    {
        var consumeInterfaces = consumerType
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
            .ToList();

        return consumeInterfaces.Count switch
        {
            0 => throw new InvalidOperationException($"{consumerType.Name} does not implement IConsume<T>"),
            > 1 => throw new InvalidOperationException(
                $"{consumerType.Name} implements multiple IConsume<T> interfaces. "
                    + "Use SubscribeFromAssembly(...) for multi-message consumers."
            ),
            _ => consumeInterfaces[0].GetGenericArguments()[0],
        };
    }
}
