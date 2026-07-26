// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Internal;
using Headless.Messaging.Registration;
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
        Bus = new BusRegistrationBuilder(this);
        Queue = new QueueRegistrationBuilder(this);
    }

    /// <summary>
    /// Gets the runtime <see cref="MessagingOptions"/> being configured.
    /// Mutate this to set value-typed configuration (intervals, batch sizes, JSON serializer
    /// options, retry policy, circuit breaker, etc.).
    /// </summary>
    public MessagingOptions Options { get; }

    /// <summary>
    /// Gets the OpenTelemetry span-enrichment configuration. Register custom
    /// <see cref="IActivityTagEnricher"/> implementations and toggle the built-in tenant-id / intent /
    /// retry-count enrichers here; the enricher pipeline runs natively at span start inside
    /// <c>Headless.Messaging.Core</c>. Subscribing an OpenTelemetry exporter (or any
    /// <see cref="System.Diagnostics.ActivityListener"/> / <see cref="System.Diagnostics.Metrics.MeterListener"/>)
    /// to the <c>Headless.Messaging</c> scope is what enables emission.
    /// </summary>
    public MessagingInstrumentationOptions Instrumentation { get; } = new();

    /// <summary>Gets the structural registration root for Bus consumers.</summary>
    public IBusRegistrationBuilder Bus { get; }

    /// <summary>Gets the structural registration root for Queue consumers.</summary>
    public IQueueRegistrationBuilder Queue { get; }

    internal IServiceCollection Services { get; }

    internal ConsumerRegistry Registry { get; }

    internal ConsumerCircuitBreakerRegistry CircuitBreakerRegistry { get; } = new();

    internal IList<IMessagesOptionsExtension> Extensions { get; } = [];

    internal void RegisterMessageRegistration(MessageRegistration registration)
    {
        Argument.IsNotNull(registration);

        var duplicateExplicitRegistration =
            !_IsAssemblyScanRegistration(registration)
            && Services.Any(descriptor =>
                descriptor.ServiceType == typeof(MessageRegistration)
                && descriptor.ImplementationInstance is MessageRegistration existing
                && !_IsAssemblyScanRegistration(existing)
                && existing.MessageType == registration.MessageType
                && existing.Lane == registration.Lane
            );
        if (duplicateExplicitRegistration)
        {
            throw new InvalidOperationException(
                $"Message type {registration.MessageType.Name} is registered more than once on lane {registration.Lane}. "
                    + "Register each message type once per lane and configure all consumers in that registration."
            );
        }

        Services.AddSingleton(registration);

        if (registration.MessageName is { } messageName)
        {
            Registry.RegisterMessageName(registration.MessageType, registration.Lane, messageName);
        }
    }

    private static bool _IsAssemblyScanRegistration(MessageRegistration registration)
    {
        return registration.Consumers.Count > 0
            && registration.Consumers.All(static consumer => consumer.IsAssemblyScan);
    }

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
    public IMessagingBuilder WithMessageNameMapping<TMessage>(string messageName)
        where TMessage : class
    {
        Argument.IsNotNullOrWhiteSpace(messageName);

        Registry.RegisterMessageName(typeof(TMessage), messageName);
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

    /// <summary>
    /// Registers a single consumer directly into the consumer registry at setup time and wires its
    /// DI descriptors.
    /// </summary>
    /// <remarks>
    /// Internal setup-time registration seam. Unlike the public <c>ForMessage&lt;T&gt;</c> surface,
    /// this does not validate <paramref name="messageName"/>, so it can register wildcard
    /// subscriptions (e.g. <c>"orders.*"</c>) that selector/runtime scenarios and their tests rely
    /// on. Kept internal deliberately — it is not dead code and must not be promoted to the public API.
    /// </remarks>
    [UsedImplicitly]
    internal ConsumerMetadata RegisterConsumer(
        Type consumerType,
        Type messageType,
        string? messageName,
        string? group,
        byte concurrency,
        MessageLane lane
    )
    {
        var metadata = Options.CreateConsumerMetadata(
            consumerType,
            messageType,
            messageName,
            Registry.TryGetRawMessageName(messageType, lane, out var mappedMessageName) ? mappedMessageName : null,
            group,
            concurrency,
            intentType: MessageLaneCompatibility.ToIntentType(lane)
        );

        Registry.Register(metadata);
        Services.TryAdd(new ServiceDescriptor(consumerType, consumerType, ServiceLifetime.Scoped));

        var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
        Services.TryAdd(
            new ServiceDescriptor(serviceType, sp => sp.GetRequiredService(consumerType), ServiceLifetime.Scoped)
        );

        return metadata;
    }
}
