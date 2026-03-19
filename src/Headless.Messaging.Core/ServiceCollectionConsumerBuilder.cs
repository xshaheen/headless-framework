// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

/// <summary>
/// Provides a fluent API for configuring consumers registered via <see cref="IServiceCollection"/> extensions.
/// </summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
/// <remarks>
/// This builder allows library authors and app developers to configure consumer behavior
/// using the same fluent pattern as <see cref="IMessagingBuilder.Subscribe{TConsumer}()"/>.
/// It directly modifies the <see cref="ConsumerMetadata"/> instance registered in DI.
/// </remarks>
public sealed class ServiceCollectionConsumerBuilder<TConsumer> : IConsumerBuilder<TConsumer>
    where TConsumer : class
{
    private readonly IServiceCollection _services;
    private ConsumerMetadata _metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceCollectionConsumerBuilder{TConsumer}"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="metadata">The consumer metadata to configure.</param>
    internal ServiceCollectionConsumerBuilder(IServiceCollection services, ConsumerMetadata metadata)
    {
        _services = services;
        _metadata = metadata;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Topic(string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        _metadata = _metadata with { Topic = topic };
        _UpdateMetadataInServices();
        return this;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Group(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        _metadata = _metadata with { Group = group };
        _UpdateMetadataInServices();
        return this;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Concurrency(byte maxConcurrent)
    {
        if (maxConcurrent == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), "Concurrency must be greater than 0");
        }

        _metadata = _metadata with { Concurrency = maxConcurrent };
        _UpdateMetadataInServices();
        return this;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> HandlerId(string handlerId)
    {
        Argument.IsNotNullOrWhiteSpace(handlerId);

        _metadata = _metadata with { HandlerId = handlerId };
        _UpdateMetadataInServices();
        return this;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        Argument.IsNotNull(configure);

        var cbOptions = new ConsumerCircuitBreakerOptions();
        configure(cbOptions);

        // Register as a singleton so Setup.cs can discover and apply it to ConsumerCircuitBreakerRegistry
        _services.AddSingleton(new ConsumerCircuitBreakerRegistration(_metadata.Group!, cbOptions));

        return this;
    }

    private void _UpdateMetadataInServices()
    {
        // Find and replace the existing metadata instance in the service collection
        var existingDescriptor = _services.FirstOrDefault(d =>
            d.ServiceType == typeof(ConsumerMetadata)
            && d.ImplementationInstance is ConsumerMetadata existing
            && existing.ConsumerType == typeof(TConsumer)
            && existing.MessageType == _metadata.MessageType
        );

        if (existingDescriptor != null)
        {
            _services.Remove(existingDescriptor);
        }

        _services.AddSingleton(_metadata);
    }
}
