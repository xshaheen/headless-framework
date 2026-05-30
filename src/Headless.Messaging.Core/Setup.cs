// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Processor;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering and configuring messaging services
/// in a <see cref="IServiceCollection"/> dependency injection container.
/// </summary>
[PublicAPI]
public static class SetupMessaging
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
    /// services.AddHeadlessMessaging(setup =>
    /// {
    ///     // Configure infrastructure
    ///     setup.Options.RetryPolicy.MaxPersistedRetries = 15;
    ///     setup.Options.SucceedMessageExpiredAfter = 24 * 3600;
    ///     setup.UseSqlServer("connection_string");
    ///     setup.UseRabbitMQ(rabbit =>
    ///     {
    ///         rabbit.HostName = "localhost";
    ///         rabbit.Port = 5672;
    ///     });
    ///
    ///     // Configure consumers
    ///     setup.SubscribeFromAssembly(typeof(Program).Assembly);
    ///
    ///     // Or register specific consumers
    ///     setup.Subscribe&lt;OrderPlacedHandler&gt;()
    ///         .MessageName("orders.placed")
    ///         .Group("order-service")
    ///         .Concurrency(5);
    ///
    ///     // Map message types to messageNames
    ///     setup.WithMessageNameMapping&lt;OrderPlaced&gt;("orders.placed");
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddHeadlessMessaging(
        this IServiceCollection services,
        Action<MessagingSetupBuilder> configure
    )
    {
        Argument.IsNotNull(configure);

        var registry = new ConsumerRegistry();
        services.TryAddSingleton<IConsumerRegistry>(registry);
        services.TryAddSingleton(registry);
        var options = new MessagingOptions();
        var setup = new MessagingSetupBuilder(services, options, registry);

        configure(setup);

        // Discover consumers registered via AddBusConsumer/AddQueueConsumer.
        _DiscoverConsumersFromDI(services, setup, registry);

        return _RegisterCoreMessagingServices(services, setup);
    }

    private static MessagingBuilder _RegisterCoreMessagingServices(
        IServiceCollection services,
        MessagingSetupBuilder setup
    )
    {
        var options = setup.Options;
        // Register the service collection itself so the bootstrapper can introspect registered descriptors
        // at startup (e.g., to detect whether IBus/IQueue publishers were gated in). This is intentional:
        // the bootstrapper is framework-internal and uses it only for read-only discovery — not as a
        // runtime factory service locator.
        services.TryAddSingleton(services);
        services.TryAddSingleton(new MessagingMarkerService("Messaging"));
        MessagingBuilder.GetOrAddMiddlewareDescriptorRegistry(services);
        services.TryAddSingleton<ILongIdGenerator, SnowflakeIdLongIdGenerator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOutboxTransactionAccessor, AsyncLocalOutboxTransactionAccessor>();
        // Tenant context primitives shared across packages — the AsyncLocal accessor + AddOrReplaceFallbackSingleton
        // wire CurrentTenant (AsyncLocal-backed) as the framework default while letting Headless.Api / EF / consumer
        // overrides supply a real implementation. NullCurrentTenant remains the fallback that's stripped when a real
        // registration appears. CurrentTenant.Id returns null when nothing populates the AsyncLocal, so the publish
        // strict-tenancy guard (#238) still fails fast when TenantContextRequired = true and no caller / seam set a tenant.
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
        services.TryAddSingleton<IMessagePublishRequestFactory, MessagePublishRequestFactory>();
        services.TryAddSingleton<OutboxMessageWriter>();
        services.TryAddSingleton<IRuntimeConsumerRegistry, RuntimeConsumerRegistry>();
        services.TryAddSingleton<IRuntimeSubscriber, RuntimeSubscriber>();

        services.TryAddSingleton<IConsumerServiceSelector, ConsumerServiceSelector>();
        services.TryAddSingleton<IConsumeMiddlewarePipeline, ConsumeMiddlewarePipeline>();
        // Singleton-with-internal-AsyncScope, mirroring IConsumeMiddlewarePipeline above. Both publishers
        // it serves are Singleton too, so a Scoped pipeline would be a captive dependency. Per-publish
        // scope is created inside ExecuteAsync so scoped publish middleware instances resolve fresh per call.
        services.TryAddSingleton<IPublishMiddlewarePipeline, PublishMiddlewarePipeline>();
        services.TryAddSingleton<ISubscribeInvoker, SubscribeInvoker>();
        services.TryAddSingleton<MethodMatcherCache>();
        services.TryAddSingleton<IMessageDispatcher, CompiledMessageDispatcher>();

        services.TryAddSingleton<IConsumerRegister, ConsumerRegister>();

        // Fallback lock provider under the messaging-scoped key. Isolated from any app-level
        // IDistributedLockProvider so UseStorageLock always targets the provider wired via
        // MessagingBuilder.UseDistributedLock(…), not an unrelated app registration.
        services.TryAddKeyedSingleton<IDistributedLockProvider, NullDistributedLockProvider>(
            MessagingKeys.LockProvider
        );

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
        services.TryAddSingleton<IRetryProcessorMonitor>(sp => sp.GetRequiredService<MessageNeedToRetryProcessor>());
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
        services.TryAddSingleton(setup.CircuitBreakerRegistry);
        services.TryAddSingleton<CircuitBreakerMetrics>();
        services.TryAddSingleton<ICircuitBreakerStateManager, CircuitBreakerStateManager>();
        services.TryAddSingleton<ICircuitBreakerMonitor>(sp => sp.GetRequiredService<ICircuitBreakerStateManager>());

        foreach (var serviceExtension in setup.Extensions)
        {
            serviceExtension.AddServices(services);
        }

        _RegisterPublisherServicesForAvailableTransports(services);

        // Register options with values that were set during AddHeadlessMessaging configuration.
        // Don't re-register setupAction as it contains consumer registration logic that
        // requires Services/Registry to be initialized - which only happens in AddHeadlessMessaging.
        services.Configure<MessagingOptions, MessagingOptionsValidator>(options.CopyTo);

        // Register and validate circuit breaker and retry processor options via DI pipeline
        services.Configure<CircuitBreakerOptions, CircuitBreakerOptionsValidator>(cb =>
            options.CircuitBreaker.CopyTo(cb)
        );
        services.Configure<RetryProcessorOptions, RetryProcessorOptionsValidator>(rp =>
            options.RetryProcessor.CopyTo(rp)
        );

        //Startup and Hosted
        services.TryAddSingleton<Bootstrapper>();
        services.TryAddSingleton<IBootstrapper>(sp => sp.GetRequiredService<Bootstrapper>());
        services.AddHostedService(sp => sp.GetRequiredService<Bootstrapper>());

        return new MessagingBuilder(services, options);
    }

    private static void _RegisterPublisherServicesForAvailableTransports(IServiceCollection services)
    {
        // Scan the service collection at registration time (extensions have already run) to determine
        // which transports are present. This gates publisher registration to transport capability so that
        // a queue-only setup never registers IBus/IOutboxBus descriptors and vice-versa.
        var hasBusTransport = services.Any(d => d.ServiceType == typeof(IBusTransport));
        var hasQueueTransport = services.Any(d => d.ServiceType == typeof(IQueueTransport));

        if (hasBusTransport)
        {
            services.TryAddSingleton<IBus>(sp => ActivatorUtilities.CreateInstance<Bus>(sp));
            services.TryAddSingleton<IOutboxBus>(sp => ActivatorUtilities.CreateInstance<OutboxBus>(sp));
        }

        if (hasQueueTransport)
        {
            services.TryAddSingleton<IQueue>(sp => ActivatorUtilities.CreateInstance<Queue>(sp));
            services.TryAddSingleton<IOutboxQueue>(sp => ActivatorUtilities.CreateInstance<OutboxQueue>(sp));
        }
    }

    /// <summary>
    /// Discovers and registers consumer metadata instances added via AddBusConsumer/AddQueueConsumer extension methods.
    /// Also applies any per-consumer circuit breaker overrides carried on the metadata.
    /// </summary>
    private static void _DiscoverConsumersFromDI(
        IServiceCollection services,
        MessagingSetupBuilder setup,
        ConsumerRegistry registry
    )
    {
        // Find all ConsumerMetadata instances registered in the service collection
        var metadataDescriptors = services
            .Where(d => d.ServiceType == typeof(ConsumerMetadata) && d.Lifetime == ServiceLifetime.Singleton)
            .ToList();

        foreach (var descriptor in metadataDescriptors)
        {
            if (descriptor.ImplementationInstance is not ConsumerMetadata metadata)
            {
                continue;
            }

            var resolved = _ResolveDiscoveredMetadata(metadata, setup.Options);
            registry.Register(resolved);

            // Apply per-consumer circuit breaker overrides inline
            if (resolved.CircuitBreakerOverride is not null && !string.IsNullOrWhiteSpace(resolved.Group))
            {
                setup.CircuitBreakerRegistry.Register(
                    CircuitBreakerGroupKeys.For(resolved),
                    resolved.CircuitBreakerOverride
                );
            }
        }
    }

    private static ConsumerMetadata _ResolveDiscoveredMetadata(ConsumerMetadata metadata, MessagingOptions options)
    {
        var resolved = options.CreateConsumerMetadata(
            metadata.ConsumerType,
            metadata.MessageType,
            metadata.MessageName,
            metadata.Group,
            metadata.Concurrency,
            metadata.HandlerId,
            metadata.IntentType
        );

        // CreateConsumerMetadata normalizes messageName/group but doesn't carry over builder-only
        // fields. If ConsumerMetadata gains new builder-set fields, copy them here too.
        return resolved with
        {
            CircuitBreakerOverride = metadata.CircuitBreakerOverride,
        };
    }
}
