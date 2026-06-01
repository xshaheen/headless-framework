// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
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
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers message-level metadata and zero or more consumers for <typeparamref name="TMessage"/>.
        /// </summary>
        /// <typeparam name="TMessage">The message type to register.</typeparam>
        /// <param name="configure">The message registration callback.</param>
        /// <returns>The current <see cref="MessagingSetupBuilder"/> instance.</returns>
        [PublicAPI]
        public MessagingSetupBuilder ForMessage<TMessage>(Action<IMessageBuilder<TMessage>> configure)
            where TMessage : class
        {
            Argument.IsNotNull(configure);

            var builder = new MessageBuilder<TMessage>(setup.Services);
            configure(builder);
            setup.Services.AddSingleton(builder.Build());

            return setup;
        }

        /// <summary>
        /// Scans the specified assembly for closed <see cref="IConsume{TMessage}"/> implementations and registers them as bus consumers.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <returns>The current <see cref="MessagingSetupBuilder"/> instance.</returns>
        [PublicAPI]
        public MessagingSetupBuilder ForMessagesFromAssembly(Assembly assembly)
        {
            Argument.IsNotNull(assembly);

            foreach (var (consumerType, messageType) in _FindConsumers(assembly))
            {
                setup.Services.TryAdd(new ServiceDescriptor(consumerType, consumerType, ServiceLifetime.Scoped));

                var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
                setup.Services.TryAdd(
                    new ServiceDescriptor(
                        serviceType,
                        sp => sp.GetRequiredService(consumerType),
                        ServiceLifetime.Scoped
                    )
                );

                setup.Services.AddSingleton(MessageRegistrationFactory.CreateScanned(messageType, consumerType));
            }

            return setup;
        }

        /// <summary>
        /// Scans the assembly containing <typeparamref name="TMarker"/> for closed <see cref="IConsume{TMessage}"/> implementations.
        /// </summary>
        /// <typeparam name="TMarker">A marker type from the target assembly.</typeparam>
        /// <returns>The current <see cref="MessagingSetupBuilder"/> instance.</returns>
        [PublicAPI]
        public MessagingSetupBuilder ForMessagesFromAssemblyContaining<TMarker>() =>
            setup.ForMessagesFromAssembly(typeof(TMarker).Assembly);
    }

    /// <summary>
    /// Registers and configures all messaging services, consumers, and transport infrastructure.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A delegate to configure messaging options, storage, transport, and message consumers.</param>
    /// <returns>A <see cref="MessagingBuilder"/> for additional messaging configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method configures messaging infrastructure and message consumers.
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
    ///     setup.ForMessage&lt;OrderPlaced&gt;(message => message
    ///         .MessageName("orders.placed")
    ///         .OnBus&lt;OrderPlacedHandler&gt;(consumer => consumer.Group("order-service").Concurrency(5)));
    ///     setup.ForMessagesFromAssemblyContaining&lt;Program&gt;();
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

        _DiscoverMessageRegistrations(services, setup, registry);

        return _RegisterCoreMessagingServices(services, setup);
    }

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

    private static void _DiscoverMessageRegistrations(
        IServiceCollection services,
        MessagingSetupBuilder setup,
        ConsumerRegistry registry
    )
    {
        var registrations = services
            .Where(static d => d.ServiceType == typeof(MessageRegistration) && d.Lifetime == ServiceLifetime.Singleton)
            .Select(static d => d.ImplementationInstance)
            .OfType<MessageRegistration>()
            .ToList();

        if (registrations.Count == 0)
        {
            return;
        }

        var explicitPairs = registrations
            .SelectMany(static registration =>
                registration
                    .Consumers.Where(static consumer => !consumer.IsAssemblyScan)
                    .Select(consumer => (registration.MessageType, consumer.ConsumerType))
            )
            .ToHashSet();

        var registeredKeys = new Dictionary<ConsumerRegistrationKey, ConsumerRegistrationSettings>();

        foreach (var group in registrations.GroupBy(static registration => registration.MessageType))
        {
            var explicitMessageNames = group
                .Select(static registration => registration.MessageName)
                .Where(static messageName => messageName is not null)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (explicitMessageNames.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Message type {group.Key.FullName ?? group.Key.Name} is already mapped to messageName '{explicitMessageNames[0]}'. "
                        + $"Cannot map to '{explicitMessageNames[1]}'."
                );
            }

            var explicitMessageName = explicitMessageNames.Count == 0 ? null : explicitMessageNames[0];

            if (explicitMessageName is not null)
            {
                setup.Options.WithMessageNameMapping(group.Key, explicitMessageName);
            }

            foreach (var registration in group)
            {
                foreach (var consumer in registration.Consumers)
                {
                    if (
                        consumer.IsAssemblyScan
                        && explicitPairs.Contains((registration.MessageType, consumer.ConsumerType))
                    )
                    {
                        continue;
                    }

                    var resolved = setup.Options.CreateConsumerMetadata(
                        consumer.ConsumerType,
                        registration.MessageType,
                        messageName: null,
                        consumer.Group,
                        consumer.Concurrency,
                        consumer.HandlerId,
                        consumer.IntentType
                    );

                    var key = new ConsumerRegistrationKey(
                        resolved.MessageName,
                        resolved.Group,
                        resolved.IntentType,
                        resolved.ConsumerType
                    );

                    var settings = new ConsumerRegistrationSettings(
                        resolved.Concurrency,
                        resolved.ResolvedHandlerId,
                        consumer.CircuitBreakerOverride is not null
                    );

                    if (registeredKeys.TryGetValue(key, out var existing))
                    {
                        // R9a: re-registering the SAME consumer for the same (message name, group, intent)
                        // is an idempotent merge only when the registration is genuinely identical. Diverging
                        // concurrency / handler id / circuit-breaker overrides would otherwise be silently
                        // dropped here, so fail fast and name the conflict instead.
                        if (existing != settings)
                        {
                            throw new InvalidOperationException(
                                $"Consumer {resolved.ConsumerType.FullName ?? resolved.ConsumerType.Name} is registered "
                                    + $"more than once for message name '{resolved.MessageName}' "
                                    + $"(group '{resolved.Group}', intent {resolved.IntentType}) with conflicting settings. "
                                    + "Register the consumer once, or make every registration identical."
                            );
                        }

                        continue;
                    }

                    registeredKeys.Add(key, settings);
                    registry.Register(resolved);
                    _ApplyCircuitBreakerOverride(setup, resolved, consumer);
                }
            }
        }
    }

    private static void _ApplyCircuitBreakerOverride(
        MessagingSetupBuilder setup,
        ConsumerMetadata resolved,
        MessageConsumerRegistration consumer
    )
    {
        if (consumer.CircuitBreakerOverride is null || string.IsNullOrWhiteSpace(resolved.Group))
        {
            return;
        }

        setup.CircuitBreakerRegistry.Register(CircuitBreakerGroupKeys.For(resolved), consumer.CircuitBreakerOverride);
    }

    private readonly record struct ConsumerRegistrationKey(
        string MessageName,
        string? Group,
        IntentType IntentType,
        Type ConsumerType
    );

    private readonly record struct ConsumerRegistrationSettings(
        byte Concurrency,
        string ResolvedHandlerId,
        bool HasCircuitBreaker
    );
}
