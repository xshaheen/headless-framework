// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Coordination;
using Headless.Messaging.Internal;
using Headless.Messaging.Processor;
using Headless.Messaging.Registration;
using Headless.Messaging.Runtime;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging;

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
            var registration = builder.Build();
            setup.Services.AddSingleton(registration);

            // Name authority is eager: register the raw name now so publish/subscribe agree without waiting
            // for the startup consumer drain. Consumer metadata still drains at bootstrap (it needs options).
            if (registration.MessageName is { } messageName)
            {
                setup.Registry.RegisterMessageName(registration.MessageType, messageName);
            }

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
            _ScanAssembly(setup, assembly, configure: null);

            return setup;
        }

        /// <summary>
        /// Scans the specified assembly for closed <see cref="IConsume{TMessage}"/> implementations and lets
        /// <paramref name="configure"/> shape each scanned consumer registration before it is registered.
        /// </summary>
        /// <param name="assembly">The assembly to scan.</param>
        /// <param name="configure">
        /// The per-consumer callback. The supplied <see cref="ScannedConsumerContext"/> exposes the discovered
        /// consumer and message types; callers should handle a null <see cref="Type.Namespace"/> when inspecting them.
        /// </param>
        /// <returns>The current <see cref="MessagingSetupBuilder"/> instance.</returns>
        [PublicAPI]
        public MessagingSetupBuilder ForMessagesFromAssembly(
            Assembly assembly,
            [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
        )
        {
            Argument.IsNotNull(assembly);
            Argument.IsNotNull(configure);

            _ScanAssembly(setup, assembly, configure);

            return setup;
        }

        /// <summary>
        /// Scans the assembly containing <typeparamref name="TMarker"/> for closed <see cref="IConsume{TMessage}"/> implementations.
        /// </summary>
        /// <typeparam name="TMarker">A marker type from the target assembly.</typeparam>
        /// <returns>The current <see cref="MessagingSetupBuilder"/> instance.</returns>
        [PublicAPI]
        public MessagingSetupBuilder ForMessagesFromAssemblyContaining<TMarker>()
        {
            return setup.ForMessagesFromAssembly(typeof(TMarker).Assembly);
        }

        /// <summary>
        /// Scans the assembly containing <typeparamref name="TMarker"/> for closed <see cref="IConsume{TMessage}"/>
        /// implementations and lets <paramref name="configure"/> shape each scanned consumer registration before it is registered.
        /// </summary>
        /// <typeparam name="TMarker">A marker type from the target assembly.</typeparam>
        /// <param name="configure">
        /// The per-consumer callback. The supplied <see cref="ScannedConsumerContext"/> exposes the discovered
        /// consumer and message types; callers should handle a null <see cref="Type.Namespace"/> when inspecting them.
        /// </param>
        /// <returns>The current <see cref="MessagingSetupBuilder"/> instance.</returns>
        [PublicAPI]
        public MessagingSetupBuilder ForMessagesFromAssemblyContaining<TMarker>(
            [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder> configure
        )
        {
            return setup.ForMessagesFromAssembly(typeof(TMarker).Assembly, configure);
        }
    }

    /// <summary>
    /// Registers message-level metadata and zero or more consumers for <typeparamref name="TMessage"/> directly on the
    /// service collection, the service-collection twin of the <c>setup.ForMessage&lt;T&gt;(…)</c> builder callback.
    /// </summary>
    /// <remarks>
    /// Use this from framework/library registration code (for example a package's <c>Add…</c> extension) that owns a
    /// consumer and must register it without access to the <see cref="MessagingSetupBuilder"/> callback. When messaging
    /// is never added, the emitted descriptors are inert. Application code should prefer the
    /// <c>setup.ForMessage&lt;T&gt;(…)</c> callback.
    /// <para>
    /// Ordering: the emitted registration is drained into the consumer registry at messaging bootstrap, so this can
    /// be called before or after <see cref="AddHeadlessMessaging"/> as long as both calls happen before host build.
    /// </para>
    /// </remarks>
    /// <typeparam name="TMessage">The message type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The message registration callback.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configure"/> is null.</exception>
    [PublicAPI]
    public static IServiceCollection ForMessage<TMessage>(
        this IServiceCollection services,
        Action<IMessageBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(configure);

        var builder = new MessageBuilder<TMessage>(services);
        configure(builder);
        var registration = builder.Build();
        services.AddSingleton(registration);

        // Name authority is eager and order-independent: the shared registry is found-or-created, so a name
        // declared here is authoritative immediately whether this seam runs before or after AddHeadlessMessaging.
        // This closes the publish-before-drain window — consumer metadata still drains at bootstrap.
        if (registration.MessageName is { } messageName)
        {
            _GetOrAddConsumerRegistry(services).RegisterMessageName(registration.MessageType, messageName);
        }

        return services;
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
    ///     setup.UseRabbitMq(rabbit =>
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

        // Found-or-created so a library that called services.ForMessage<T>(...) before AddHeadlessMessaging
        // shares the same registry instance — registration order does not matter.
        var registry = _GetOrAddConsumerRegistry(services);
        var options = new MessagingOptions();
        var setup = new MessagingSetupBuilder(services, options, registry);

        configure(setup);

        return _RegisterCoreMessagingServices(services, setup);
    }

    private static ConsumerRegistry _GetOrAddConsumerRegistry(IServiceCollection services)
    {
        if (
            services.FirstOrDefault(static d => d.ServiceType == typeof(ConsumerRegistry))?.ImplementationInstance
            is ConsumerRegistry existing
        )
        {
            return existing;
        }

        var registry = new ConsumerRegistry();
        services.AddSingleton(registry);
        services.TryAddSingleton<IConsumerRegistry>(registry);

        return registry;
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

    private static void _ScanAssembly(
        MessagingSetupBuilder setup,
        Assembly assembly,
        [InstantHandle] Action<ScannedConsumerContext, IScannedConsumerBuilder>? configure
    )
    {
        Argument.IsNotNull(assembly);

        foreach (var (consumerType, messageType) in _FindConsumers(assembly))
        {
            var builder = new ScannedConsumerBuilder(consumerType);
            configure?.Invoke(new ScannedConsumerContext(consumerType, messageType), builder);

            if (builder.IsSkipped)
            {
                continue;
            }

            setup.Services.TryAdd(new ServiceDescriptor(consumerType, consumerType, ServiceLifetime.Scoped));

            var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
            setup.Services.TryAdd(
                new ServiceDescriptor(serviceType, sp => sp.GetRequiredService(consumerType), ServiceLifetime.Scoped)
            );

            setup.Services.AddSingleton(
                new MessageRegistration(
                    messageType,
                    MessageName: null,
                    CorrelationSelector: null,
                    ProviderConfigs: new Dictionary<Type, object>(),
                    Consumers: [builder.Build()]
                )
            );
        }
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
        services.AddHeadlessGuidGenerator();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CommitCoordination.ICurrentCommitCoordinator, MessagingNullCommitCoordinator>();
        // Tenant context primitives shared across packages — the AsyncLocal accessor + AddOrReplaceFallbackSingleton
        // wire CurrentTenant (AsyncLocal-backed) as the framework default while letting Headless.Api / EF / consumer
        // overrides supply a real implementation. NullCurrentTenant remains the fallback that's stripped when a real
        // registration appears. CurrentTenant.Id returns null when nothing populates the AsyncLocal, so the publish
        // strict-tenancy guard (#238) still fails fast when TenantContextRequired = true and no caller / seam set a tenant.
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
        services.TryAddSingleton<IMessageMetadataRegistry, MessageMetadataRegistry>();
        services.TryAddSingleton<IConsumeContextAccessor, AsyncLocalConsumeContextAccessor>();
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
        // IDistributedLock so UseStorageLock always targets the provider wired via
        // MessagingBuilder.UseDistributedLock(…), not an unrelated app registration.
        services.TryAddKeyedSingleton<IDistributedLock, NullDistributedLock>(MessagingKeys.LockProvider);
        services.TryAddSingleton<INodeMembership, NullNodeMembership>();

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

        // Dead-owner recovery bridge: always-on, decoupled from UseStorageLock (KTD3). When no real
        // INodeMembership is wired the registered NullNodeMembership makes the bridge a benign no-op
        // (empty snapshot, no NodeLeft events). Cross-node safety rests on the owner-scoped conditional
        // reclaim UPDATE being idempotent, not on a held lock.
        services.TryAddSingleton<MessagingDeadOwnerReclaimer>();
        services.AddHostedService<DeadOwnerRecoveryBridge<MessagingDeadOwnerReclaimer>>();

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
    /// Drains the deferred <see cref="MessageRegistration"/> singletons captured by <c>ForMessage&lt;T&gt;</c> into the
    /// consumer registry. Order-independent: the registrations are resolved from the built provider, so it works whether
    /// the <c>ForMessage</c> / <c>Add…</c> call ran before or after <see cref="AddHeadlessMessaging"/>. Idempotent — the
    /// first caller (Bootstrapper startup or the consumer selector) wins; subsequent calls are no-ops.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Threading invariant.</strong> The <c>HasCompletedMessageRegistrationDrain</c> guard provides
    /// idempotency, not thread-safety: the guard read, the consumer-registration loop, and the completion mark are
    /// not one atomic step. This is safe only because the two callers never drain concurrently. The bootstrapper
    /// drains synchronously in <c>_CheckRequirement</c> before it starts any processor, so message dispatch — and
    /// therefore the consumer-selector fallback — cannot run during the bootstrap drain; and concurrent
    /// <c>BootstrapAsync</c> callers share a single in-flight task. The selector path only performs the first drain
    /// in manual/test hosts that bypass the bootstrapper.
    /// </para>
    /// <para>
    /// If a future change starts a processor (or otherwise dispatches a message) before this drain completes, two
    /// threads can pass the guard and double-register a consumer, throwing a spurious "Duplicate consumer
    /// registration". Keep the bootstrapper drain ahead of processor startup, or make the drain atomic.
    /// </para>
    /// </remarks>
    internal static void DrainPendingMessageRegistrations(IServiceProvider provider, MessagingOptions options)
    {
        var registry = provider.GetRequiredService<ConsumerRegistry>();

        if (registry.HasCompletedMessageRegistrationDrain)
        {
            // Guard: detect ForMessage<T> calls that happened after BuildServiceProvider(). On .NET 10,
            // ServiceCollection is not made read-only after build — mutations silently succeed but the
            // existing provider never sees the new descriptors. Compare the descriptor count in the
            // registered IServiceCollection (snapshot from before build) against what the provider can
            // actually resolve to surface this misuse as a loud warning rather than a silent no-op.
            var serviceCollection = provider.GetService<IServiceCollection>();

            if (serviceCollection is not null)
            {
                var descriptorCount = serviceCollection.Count(static d => d.ServiceType == typeof(MessageRegistration));
                var resolvedCount = provider.GetServices<MessageRegistration>().Count();

                if (descriptorCount > resolvedCount)
                {
                    var logger = provider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SetupMessaging).FullName!);

                    logger?.ForMessageCalledAfterProviderBuilt(descriptorCount - resolvedCount);
                }
            }

            return;
        }

        var registrations = provider.GetServices<MessageRegistration>().ToList();

        // Nothing was captured via ForMessage<T> — mark drained without touching the circuit-breaker
        // registry, which is only registered once AddHeadlessMessaging's core wiring has run.
        if (registrations.Count == 0)
        {
            registry.MarkMessageRegistrationDrainCompleted();
            return;
        }

        var circuitBreakerRegistry = provider.GetRequiredService<ConsumerCircuitBreakerRegistry>();

        DiscoverMessageRegistrations(registrations, options, registry, circuitBreakerRegistry);
    }

    internal static void DiscoverMessageRegistrations(
        IReadOnlyCollection<MessageRegistration> registrations,
        MessagingOptions options,
        ConsumerRegistry registry,
        ConsumerCircuitBreakerRegistry circuitBreakerRegistry
    )
    {
        if (registry.HasCompletedMessageRegistrationDrain)
        {
            return;
        }

        if (registrations.Count == 0)
        {
            registry.MarkMessageRegistrationDrainCompleted();
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

        // Names are registered eagerly at ForMessage<T>(...) time, so the drain only builds consumer
        // metadata (which needs MessagingOptions). Iterate registrations directly — no per-type grouping.
        foreach (var registration in registrations)
        {
            foreach (var consumer in registration.Consumers)
            {
                if (
                    consumer.IsAssemblyScan && explicitPairs.Contains((registration.MessageType, consumer.ConsumerType))
                )
                {
                    continue;
                }

                var resolved = options.CreateConsumerMetadata(
                    consumer.ConsumerType,
                    registration.MessageType,
                    messageName: null,
                    registry.TryGetRawMessageName(registration.MessageType, out var mappedMessageName)
                        ? mappedMessageName
                        : null,
                    consumer.Group,
                    consumer.Concurrency,
                    consumer.HandlerId,
                    consumer.IntentType
                ) with
                {
                    ProviderConfigs = consumer.ProviderConfigs,
                };

                var key = new ConsumerRegistrationKey(
                    resolved.MessageName,
                    resolved.Group,
                    resolved.IntentType,
                    resolved.ConsumerType
                );

                var settings = new ConsumerRegistrationSettings(
                    resolved.Concurrency,
                    resolved.ResolvedHandlerId,
                    ConsumerCircuitBreakerSettings.From(consumer.CircuitBreakerOverride),
                    resolved.ProviderConfigs
                );

                if (registeredKeys.TryGetValue(key, out var existing))
                {
                    // R9a: re-registering the SAME consumer for the same (message name, group, intent)
                    // is an idempotent merge only when the registration is genuinely identical. Diverging
                    // concurrency / handler id / circuit-breaker / provider overrides would otherwise be silently
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
                _ApplyCircuitBreakerOverride(circuitBreakerRegistry, resolved, consumer);
            }
        }

        registry.MarkMessageRegistrationDrainCompleted();
    }

    private static void _ApplyCircuitBreakerOverride(
        ConsumerCircuitBreakerRegistry circuitBreakerRegistry,
        ConsumerMetadata resolved,
        MessageConsumerRegistration consumer
    )
    {
        if (consumer.CircuitBreakerOverride is null || string.IsNullOrWhiteSpace(resolved.Group))
        {
            return;
        }

        circuitBreakerRegistry.Register(CircuitBreakerGroupKeys.For(resolved), consumer.CircuitBreakerOverride);
    }

    private readonly record struct ConsumerRegistrationKey(
        string MessageName,
        string? Group,
        IntentType IntentType,
        Type ConsumerType
    )
    {
        // Message names are matched case-insensitively at dispatch, so the dedup key must treat
        // case-variant names as identical. Groups stay case-sensitive (Ordinal everywhere else).
        public bool Equals(ConsumerRegistrationKey other)
        {
            return IntentType == other.IntentType
                && ConsumerType == other.ConsumerType
                && string.Equals(MessageName, other.MessageName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Group, other.Group, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(MessageName),
                Group is null ? 0 : StringComparer.Ordinal.GetHashCode(Group),
                IntentType,
                ConsumerType
            );
        }
    }

    private readonly struct ConsumerRegistrationSettings(
        byte concurrency,
        string resolvedHandlerId,
        ConsumerCircuitBreakerSettings circuitBreaker,
        IReadOnlyDictionary<Type, object> providerConfigs
    ) : IEquatable<ConsumerRegistrationSettings>
    {
        private readonly byte _concurrency = concurrency;
        private readonly string _resolvedHandlerId = resolvedHandlerId;
        private readonly ConsumerCircuitBreakerSettings _circuitBreaker = circuitBreaker;
        private readonly IReadOnlyDictionary<Type, object> _providerConfigs = providerConfigs;

        public bool Equals(ConsumerRegistrationSettings other)
        {
            return _concurrency == other._concurrency
                && string.Equals(_resolvedHandlerId, other._resolvedHandlerId, StringComparison.Ordinal)
                && _circuitBreaker == other._circuitBreaker
                && _ProviderConfigsEqual(_providerConfigs, other._providerConfigs);
        }

        public override bool Equals(object? obj)
        {
            return obj is ConsumerRegistrationSettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_concurrency);
            hash.Add(_resolvedHandlerId, StringComparer.Ordinal);
            hash.Add(_circuitBreaker);

            foreach (var pair in _providerConfigs.OrderBy(static pair => pair.Key.FullName, StringComparer.Ordinal))
            {
                hash.Add(pair.Key);
                hash.Add(pair.Value);
            }

            return hash.ToHashCode();
        }

        public static bool operator ==(ConsumerRegistrationSettings left, ConsumerRegistrationSettings right) =>
            left.Equals(right);

        public static bool operator !=(ConsumerRegistrationSettings left, ConsumerRegistrationSettings right) =>
            !left.Equals(right);

        private static bool _ProviderConfigsEqual(
            IReadOnlyDictionary<Type, object> left,
            IReadOnlyDictionary<Type, object> right
        )
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var (key, value) in left)
            {
                if (!right.TryGetValue(key, out var otherValue))
                {
                    return false;
                }

                // Class-based message-scope configs (e.g. KafkaMessageConfig<T>) implement
                // IProviderHeaderContributions and hold a Func — they have no meaningful value
                // equality. Two instances of the same type for the same key are idempotent.
                if (value is IProviderHeaderContributions)
                {
                    continue;
                }

                if (!Equals(value, otherValue))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private readonly record struct ConsumerCircuitBreakerSettings(
        bool HasOverride,
        bool Enabled,
        int? FailureThreshold,
        TimeSpan? OpenDuration,
        Func<Exception, bool>? IsTransientException
    )
    {
        public static ConsumerCircuitBreakerSettings From(ConsumerCircuitBreakerOptions? options)
        {
            return options is null
                ? default
                : new ConsumerCircuitBreakerSettings(
                    HasOverride: true,
                    options.Enabled,
                    options.FailureThreshold,
                    options.OpenDuration,
                    options.IsTransientException
                );
        }
    }
}

internal static partial class SetupMessagingLog
{
    [LoggerMessage(
        EventId = 88,
        EventName = "ForMessageCalledAfterProviderBuilt",
        Level = LogLevel.Warning,
        Message = "{Count} ForMessage<T> registration(s) were added after the service provider was built and will be ignored. Move all ForMessage<T> calls before BuildServiceProvider() / host.Build()."
    )]
    public static partial void ForMessageCalledAfterProviderBuilt(this ILogger logger, int count);
}
