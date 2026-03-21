// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Processor;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering and configuring messaging services
/// in a <see cref="IServiceCollection"/> dependency injection container.
/// </summary>
public static class Setup
{
    /// <summary>
    /// Registers and configures all messaging services, consumers, and transport infrastructure.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A delegate to configure messaging options, storage, transport, and message consumers.</param>
    /// <returns>A <see cref="MessagingBuilder"/> for additional messaging configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method provides a unified API for configuring both messaging infrastructure (storage, transport, retry policies)
    /// and message consumers that implement <see cref="IConsume{TMessage}"/>.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// services.AddHeadlessMessaging(options =>
    /// {
    ///     // Configure infrastructure
    ///     options.FailedRetryCount = 50;
    ///     options.SucceedMessageExpiredAfter = 24 * 3600;
    ///     options.UseSqlServer("connection_string");
    ///     options.UseRabbitMQ(rabbit =>
    ///     {
    ///         rabbit.HostName = "localhost";
    ///         rabbit.Port = 5672;
    ///     });
    ///
    ///     // Configure consumers
    ///     options.SubscribeFromAssembly(typeof(Program).Assembly);
    ///
    ///     // Or register specific consumers
    ///     options.Subscribe&lt;OrderPlacedHandler&gt;()
    ///         .Topic("orders.placed")
    ///         .Group("order-service")
    ///         .Concurrency(5);
    ///
    ///     // Map message types to topics
    ///     options.WithTopicMapping&lt;OrderPlaced&gt;("orders.placed");
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddHeadlessMessaging(this IServiceCollection services, Action<MessagingOptions> configure)
    {
        Argument.IsNotNull(configure);

        var registry = new ConsumerRegistry();
        services.TryAddSingleton<IConsumerRegistry>(registry);
        services.TryAddSingleton(registry);
        var options = new MessagingOptions { Services = services, Registry = registry };

        configure(options);

        // Discover consumers registered via AddConsumer<TConsumer, TMessage>()
        _DiscoverConsumersFromDI(services, options, registry);

        // Discover per-consumer circuit breaker registrations added via AddConsumer().WithCircuitBreaker()
        _DiscoverCircuitBreakerRegistrationsFromDI(services, options, options.CircuitBreakerRegistry);

        return _RegisterCoreMessagingServices(services, options);
    }

    private static MessagingBuilder _RegisterCoreMessagingServices(
        IServiceCollection services,
        MessagingOptions options
    )
    {
        services.AddSingleton(_ => services);
        services.TryAddSingleton(new MessagingMarkerService("Messaging"));
        services.TryAddSingleton<ILongIdGenerator, SnowflakeIdLongIdGenerator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOutboxTransactionAccessor, AsyncLocalOutboxTransactionAccessor>();
        services.TryAddSingleton<IMessagePublishRequestFactory, MessagePublishRequestFactory>();
        services.TryAddSingleton<OutboxPublisher>();
        services.TryAddSingleton<IOutboxPublisher>(sp => sp.GetRequiredService<OutboxPublisher>());
        services.TryAddSingleton<IScheduledPublisher>(sp => sp.GetRequiredService<OutboxPublisher>());
        services.TryAddSingleton<IDirectPublisher, DirectPublisher>();
        services.TryAddSingleton<IRuntimeConsumerRegistry, RuntimeConsumerRegistry>();
        services.TryAddSingleton<IRuntimeSubscriber, RuntimeSubscriber>();

        services.TryAddSingleton<IConsumerServiceSelector, ConsumerServiceSelector>();
        services.TryAddSingleton<IConsumeExecutionPipeline, ConsumeExecutionPipeline>();
        services.TryAddSingleton<ISubscribeInvoker, SubscribeInvoker>();
        services.TryAddSingleton<MethodMatcherCache>();
        services.TryAddSingleton<IMessageDispatcher, CompiledMessageDispatcher>();

        services.TryAddSingleton<IConsumerRegister, ConsumerRegister>();

        //Processors
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProcessingServer, IDispatcher>(sp => sp.GetRequiredService<IDispatcher>())
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProcessingServer, IConsumerRegister>(sp =>
                sp.GetRequiredService<IConsumerRegister>()
            )
        );
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProcessingServer, MessageProcessingServer>());

        //Queue's message processor
        services.TryAddSingleton<MessageNeedToRetryProcessor>();
        services.TryAddSingleton<TransportCheckProcessor>();
        services.TryAddSingleton<MessageDelayedProcessor>();
        services.TryAddSingleton<CollectorProcessor>();

        //Sender
        services.TryAddSingleton<IMessageSender, MessageSender>();

        services.TryAddSingleton<ISerializer, JsonUtf8Serializer>();

        // Warning: IPublishMessageSender need to inject at extension project.
        services.TryAddSingleton<ISubscribeExecutor, SubscribeExecutor>();

        services.TryAddSingleton<IDispatcher, Dispatcher>();

        // Circuit breaker
        services.AddMetrics();
        services.TryAddSingleton(options.CircuitBreakerRegistry);
        services.TryAddSingleton<CircuitBreakerMetrics>();
        services.TryAddSingleton<ICircuitBreakerStateManager, CircuitBreakerStateManager>();
        services.TryAddSingleton<ICircuitBreakerMonitor>(sp =>
            (ICircuitBreakerMonitor)sp.GetRequiredService<ICircuitBreakerStateManager>());

        foreach (var serviceExtension in options.Extensions)
        {
            serviceExtension.AddServices(services);
        }

        // Register options with values that were set during AddHeadlessMessaging configuration.
        // Don't re-register setupAction as it contains consumer registration logic that
        // requires Services/Registry to be initialized - which only happens in AddHeadlessMessaging.
        services.Configure<MessagingOptions>(opt =>
        {
            opt.Services = services;
            opt.Registry = options.Registry;

            // Copy public properties
            opt.DefaultGroupName = options.DefaultGroupName;
            opt.GroupNamePrefix = options.GroupNamePrefix;
            opt.TopicNamePrefix = options.TopicNamePrefix;
            opt.Version = options.Version;
            opt.Conventions = options.Conventions;
            opt.SucceedMessageExpiredAfter = options.SucceedMessageExpiredAfter;
            opt.FailedMessageExpiredAfter = options.FailedMessageExpiredAfter;
            opt.FailedRetryInterval = options.FailedRetryInterval;
            opt.FailedThresholdCallback = options.FailedThresholdCallback;
            opt.FailedRetryCount = options.FailedRetryCount;
            opt.ConsumerThreadCount = options.ConsumerThreadCount;
            opt.EnableSubscriberParallelExecute = options.EnableSubscriberParallelExecute;
            opt.SubscriberParallelExecuteThreadCount = options.SubscriberParallelExecuteThreadCount;
            opt.SubscriberParallelExecuteBufferFactor = options.SubscriberParallelExecuteBufferFactor;
            opt.EnablePublishParallelSend = options.EnablePublishParallelSend;
            opt.PublishBatchSize = options.PublishBatchSize;
            opt.FallbackWindowLookbackSeconds = options.FallbackWindowLookbackSeconds;
            opt.CollectorCleaningInterval = options.CollectorCleaningInterval;
            opt.SchedulerBatchSize = options.SchedulerBatchSize;
            opt.UseStorageLock = options.UseStorageLock;
            opt.RetryBackoffStrategy = options.RetryBackoffStrategy;

            // Copy internal collections
            foreach (var mapping in options.TopicMappings)
            {
                opt.TopicMappings[mapping.Key] = mapping.Value;
            }
        });

        // Register and validate circuit breaker and retry processor options via DI pipeline
        services.Configure<CircuitBreakerOptions, CircuitBreakerOptionsValidator>(cb =>
        {
            cb.FailureThreshold = options.CircuitBreaker.FailureThreshold;
            cb.OpenDuration = options.CircuitBreaker.OpenDuration;
            cb.MaxOpenDuration = options.CircuitBreaker.MaxOpenDuration;
            cb.SuccessfulCyclesToResetEscalation = options.CircuitBreaker.SuccessfulCyclesToResetEscalation;
            cb.IsTransientException = options.CircuitBreaker.IsTransientException;
        });
        services.Configure<RetryProcessorOptions, RetryProcessorOptionsValidator>(rp =>
        {
            rp.AdaptivePolling = options.RetryProcessor.AdaptivePolling;
            rp.MaxPollingInterval = options.RetryProcessor.MaxPollingInterval;
            rp.CircuitOpenRateThreshold = options.RetryProcessor.CircuitOpenRateThreshold;
        });

        //Startup and Hosted
        services.TryAddSingleton<Bootstrapper>();
        services.TryAddSingleton<IBootstrapper>(sp => sp.GetRequiredService<Bootstrapper>());
        services.AddHostedService(sp => sp.GetRequiredService<Bootstrapper>());

        return new MessagingBuilder(services);
    }

    /// <summary>
    /// Discovers and registers consumer metadata instances added via AddConsumer extension method.
    /// </summary>
    private static void _DiscoverConsumersFromDI(
        IServiceCollection services,
        MessagingOptions options,
        ConsumerRegistry registry
    )
    {
        // Find all ConsumerMetadata instances registered in the service collection
        var metadataDescriptors = services
            .Where(d => d.ServiceType == typeof(ConsumerMetadata) && d.Lifetime == ServiceLifetime.Singleton)
            .ToList();

        foreach (var descriptor in metadataDescriptors)
        {
            if (descriptor.ImplementationInstance is ConsumerMetadata metadata)
            {
                registry.Register(_ResolveDiscoveredMetadata(metadata, options));
            }
        }
    }

    /// <summary>
    /// Discovers per-consumer circuit breaker registrations added via
    /// <c>AddConsumer().WithCircuitBreaker()</c> and applies them to the registry.
    /// </summary>
    private static void _DiscoverCircuitBreakerRegistrationsFromDI(
        IServiceCollection services,
        MessagingOptions options,
        ConsumerCircuitBreakerRegistry circuitBreakerRegistry
    )
    {
        var registrationDescriptors = services
            .Where(d =>
                d.ServiceType == typeof(ConsumerCircuitBreakerRegistration)
                && d.Lifetime == ServiceLifetime.Singleton
            )
            .ToList();

        foreach (var descriptor in registrationDescriptors)
        {
            if (descriptor.ImplementationInstance is not ConsumerCircuitBreakerRegistration registration)
            {
                continue;
            }

            var groupName = registration.GroupName;

            // If no group was set on the builder, resolve from the final consumer metadata in the registry
            if (string.IsNullOrWhiteSpace(groupName))
            {
                var metadata = options.Registry?.FindByTypes(registration.ConsumerType, registration.MessageType);
                groupName = metadata?.Group;
            }

            if (!string.IsNullOrWhiteSpace(groupName))
            {
                circuitBreakerRegistry.Register(groupName, registration.Options);
            }
        }
    }

    private static ConsumerMetadata _ResolveDiscoveredMetadata(ConsumerMetadata metadata, MessagingOptions options)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Group))
        {
            return metadata;
        }

        options.Conventions.Version = options.Version;
        return metadata with { Group = options.Conventions.GetGroupName(metadata.ResolvedHandlerId) };
    }

}
