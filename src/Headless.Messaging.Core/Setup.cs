// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
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
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

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
    ///     setup.Bus.ForMessage&lt;OrderPlaced&gt;(message => message
    ///         .Contract("orders.placed")
    ///         .Consumer&lt;OrderPlacedHandler&gt;(consumer => consumer
    ///             .ConsumerIdentity("order-service.order-placed")
    ///             .Group("order-service")
    ///             .Concurrency(5)));
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

        // Found-or-created so ConfigureMessaging contributions and setup-time extensions share one registry instance.
        var registry = GetOrAddConsumerRegistry(services);
        var options = new MessagingOptions();
        var setup = new MessagingSetupBuilder(services, options, registry);

        configure(setup);

        return _RegisterCoreMessagingServices(services, setup);
    }

    internal static ConsumerRegistry GetOrAddConsumerRegistry(IServiceCollection services)
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

    private static MessagingBuilder _RegisterCoreMessagingServices(
        IServiceCollection services,
        MessagingSetupBuilder setup
    )
    {
        var options = setup.Options;
        services.TryAddSingleton(new MessagingMarkerService("Messaging"));
        MessagingBuilder.GetOrAddMiddlewareDescriptorRegistry(services);
        services.AddHeadlessGuidGenerator();
        services.TryAddSingleton(TimeProvider.System);
        // Idempotent: registers the singleton IUnitOfWorkFactory exactly once regardless of registration order
        // with other consumer packages (Headless.EntityFramework, Headless.Jobs.Core).
        services.AddUnitOfWork();
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
        services.TryAddSingleton(sp =>
            MessagingCapabilityModel.Compose(sp.GetServices<MessagingProviderCapabilities>())
        );
        services.TryAddSingleton<IMessagingCapabilityModel>(sp => sp.GetRequiredService<MessagingCapabilityModel>());
        services.TryAddSingleton<IMessageCapabilityGate>(sp => sp.GetRequiredService<MessagingCapabilityModel>());

        // Native OpenTelemetry emitter: the enricher snapshot (built-ins gated by the Suppress* toggles plus any
        // custom enrichers) is captured once here from the setup-time instrumentation config. Instruments and the
        // ActivitySource are near-free until an exporter subscribes to the Headless.Messaging scope.
        var messagingEnrichers = setup.Instrumentation.BuildEnrichers();
        services.TryAddSingleton(new InboxMetricPolicy(setup.Instrumentation.IncludeTenantIdInMetricTags));
        services.TryAddSingleton(sp => new MessagingTelemetry(
            messagingEnrichers,
            sp.GetService<ILogger<MessagingTelemetry>>()
        ));

        services.TryAddSingleton<OutboxMessageWriter>();
        services.TryAddSingleton<IMessageRevoker, MessageRevoker>();
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

        // Dead-owner recovery bridge: always-on, decoupled from UseStorageLock. When no real
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

        // Generic publisher facades are provider-order independent. They resolve transport/storage implementations
        // only after the immutable capability gate accepts the individual call.
        services.TryAddSingleton(sp =>
        {
            ITransport ResolveTransport(MessageLane lane) =>
                lane switch
                {
                    MessageLane.Bus => sp.GetRequiredService<IBusTransport>(),
                    MessageLane.Queue => sp.GetRequiredService<IQueueTransport>(),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(lane),
                        lane,
                        "A defined messaging lane is required."
                    ),
                };

            return new MessagePublisher(
                sp.GetRequiredService<ISerializer>(),
                ResolveTransport,
                sp.GetRequiredService<IMessagePublishRequestFactory>(),
                sp.GetRequiredService<IPublishMiddlewarePipeline>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IMessageCapabilityGate>(),
                () => sp.GetService<IDeliveryCoordinationResolver>(),
                () => sp.GetService<OutboxMessageWriter>(),
                sp.GetService<MessagingTelemetry>(),
                options.TransportPublishTimeout,
                sp.GetRequiredService<IOptions<MessagingOptions>>().Value.DefaultDeliveryMode,
                sp.GetServices<MessageRegistration>()
            );
        });
        // Singleton: these facades are autonomous, so they hold no scope-bound state and framework singletons
        // (HybridCache, the distributed lock primitives) can depend on them. Enlisting a publish in a caller's
        // active unit of work is the unit-of-work outbox's job, reached from the unit itself.
        services.TryAddSingleton<IBus>(sp => new Bus(sp.GetRequiredService<MessagePublisher>()));
        services.TryAddSingleton<IQueue>(sp => new Queue(sp.GetRequiredService<MessagePublisher>()));

        // The other half of that split: unit.Outbox resolves this feature from the unit's scope, so a publish
        // reached from a unit of work always enlists in it and refuses when the storage cannot join. A singleton
        // like the publisher it wraps; the unit it enlists in arrives as an argument on every call.
        services.TryAddSingleton<IUnitOfWorkOutbox>(sp => new UnitOfWorkOutboxFeature(
            sp.GetRequiredService<MessagePublisher>()
        ));

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

        var consumeOnly = setup.FreezeConsumeOnly();
        if (consumeOnly.Length != 0)
        {
            services.AddSingleton(new MessagingConsumeOnlyContribution(consumeOnly));
        }

        //Startup and Hosted
        services.TryAddSingleton<Bootstrapper>();
        services.TryAddSingleton<IBootstrapper>(sp => sp.GetRequiredService<Bootstrapper>());
        services.AddHostedService(sp => sp.GetRequiredService<Bootstrapper>());

        return new MessagingBuilder(services, options);
    }

    /// <summary>
    /// Drains the deferred <see cref="MessageRegistration"/> descriptors, in registration order, into the consumer
    /// registry. Order-independent: the descriptors are resolved from the built provider, so a
    /// <see cref="MessagingContributionExtensions.ConfigureMessaging"/> contribution counts whether it ran before or
    /// after <see cref="AddHeadlessMessaging"/>. Idempotent — the
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
            return;
        }

        // Every source records MessageRegistration descriptors: the AddHeadlessMessaging callback, ConfigureMessaging
        // contributions, and framework consumers. The container returns them in registration order. Generated modules
        // register last, so their consumers read the contract versions those registrations declare.
        var registrations = provider.GetServices<MessageRegistration>().ToList();
        registrations.AddRange(
            CreateModuleRegistrations(provider.GetServices<MessagingModuleContribution>(), registrations)
        );

        var controls = new MessagingHostControls(
            [.. provider.GetServices<MessagingTuningContribution>().Select(static x => x.Tuning)],
            [.. provider.GetServices<MessagingConsumeOnlyContribution>().SelectMany(static x => x.Entries)],
            provider.GetService<IConfiguration>()
        );

        // The circuit-breaker registry is only registered once AddHeadlessMessaging's core wiring has run, so it is
        // resolved only when something was captured that may carry a circuit-breaker override.
        var circuitBreakerRegistry =
            registrations.Count == 0 ? null : provider.GetRequiredService<ConsumerCircuitBreakerRegistry>();

        DiscoverMessageRegistrations(registrations, options, registry, circuitBreakerRegistry, controls);
    }

    /// <summary>
    /// Runs every added generated module once, however many times it was added, and turns each consumer it declares into
    /// a consumer-only registration of its message on its lane.
    /// </summary>
    /// <remarks>
    /// A generated consumer carries no message-level settings, so it takes its message's contract version from the
    /// registration that declares the message on that lane, such as a <c>Message&lt;T&gt;(name, version)</c> contract,
    /// and the initial version when nothing declares it.
    /// </remarks>
    internal static IEnumerable<MessageRegistration> CreateModuleRegistrations(
        IEnumerable<MessagingModuleContribution> modules,
        IReadOnlyCollection<MessageRegistration> declared
    )
    {
        var catalog = new MessagingCatalogBuilder();
        var added = new HashSet<Type>();
        foreach (var module in modules)
        {
            if (added.Add(module.ModuleType))
            {
                catalog.AddModule(module.ModuleType, module.Register);
            }
        }

        if (catalog.Consumers.Count == 0)
        {
            return [];
        }

        var contractVersions = new Dictionary<(Type MessageType, MessageLane Lane), string>();
        foreach (var registration in declared.Where(static registration => registration.DeclaresMessage))
        {
            contractVersions.TryAdd((registration.MessageType, registration.Lane), registration.ContractVersion);
        }

        return catalog.Consumers.Select(consumer =>
            MessageRegistration.ConsumerOnly(
                consumer.MessageType,
                consumer.Lane,
                messageName: null,
                new MessageConsumerRegistration(
                    consumer.ConsumerType,
                    consumer.Lane,
                    IsAssemblyScan: false,
                    Group: null,
                    Concurrency: 1,
                    HandlerId: null,
                    consumer.Identity,
                    CircuitBreakerOverride: null,
                    ProviderConfigs: new Dictionary<Type, object>()
                )
                {
                    Dispatch = consumer.Dispatch,
                    EveryInstance = consumer.EveryInstance,
                    FailurePolicy = consumer.Policy,
                    DeclaringModule = consumer.Source,
                },
                contractVersions.GetValueOrDefault(
                    (consumer.MessageType, consumer.Lane),
                    MessageOptions.InitialContractVersion
                )
            )
        );
    }

    /// <summary>
    /// Builds the host's consumers from the drained registrations, then, in order: detects identity, Queue, and route
    /// conflicts; applies <c>Tune</c> calls and <c>Headless:Messaging:Consumers:{identity}</c> configuration; and
    /// resolves the <c>ConsumeOnly</c> filter.
    /// </summary>
    internal static void DiscoverMessageRegistrations(
        IReadOnlyCollection<MessageRegistration> registrations,
        MessagingOptions options,
        ConsumerRegistry registry,
        ConsumerCircuitBreakerRegistry? circuitBreakerRegistry,
        MessagingHostControls controls
    )
    {
        if (registry.HasCompletedMessageRegistrationDrain)
        {
            return;
        }

        var explicitPairs = registrations
            .SelectMany(static registration =>
                registration
                    .Consumers.Where(static consumer => !consumer.IsAssemblyScan)
                    .Select(consumer => (registration.MessageType, consumer.ConsumerType, registration.Lane))
            )
            .ToHashSet();

        var registeredKeys = new Dictionary<ConsumerRegistrationKey, ConsumerRegistrationSettings>();
        var consumers = new List<(ConsumerMetadata Metadata, ConsumerCircuitBreakerOptions? CircuitBreakerOverride)>();

        // Names are registered eagerly at lane-owned registration time, so the drain only builds consumer
        // metadata (which needs MessagingOptions). Iterate registrations directly — no per-type grouping.
        foreach (var registration in registrations)
        {
            foreach (var consumer in registration.Consumers)
            {
                if (
                    consumer.IsAssemblyScan
                    && explicitPairs.Contains((registration.MessageType, consumer.ConsumerType, registration.Lane))
                )
                {
                    continue;
                }

                var resolved = options.CreateConsumerMetadata(
                    consumer.ConsumerType,
                    registration.MessageType,
                    registration.MessageName,
                    registry.TryGetRawMessageName(
                        registration.MessageType,
                        registration.Lane,
                        out var mappedMessageName
                    )
                        ? mappedMessageName
                        : null,
                    consumer.Group,
                    consumer.Concurrency,
                    consumer.HandlerId,
                    consumer.ConsumerIdentity,
                    registration.ContractVersion,
                    registration.Lane
                ) with
                {
                    ProviderConfigs = consumer.ProviderConfigs,
                    InboxRetention = consumer.InboxRetention ?? TimeSpan.FromDays(30),
                    EveryInstance = consumer.EveryInstance,
                    FailurePolicy = consumer.FailurePolicy,
                    Dispatch = consumer.Dispatch,
                    DeclaringModule = consumer.DeclaringModule,
                };

                // An attribute-declared consumer subscribes under its identity on the Bus lane, so one client binds every
                // message the identity covers, and under its message name on the Queue lane, which has one consumer per
                // message. Consumers registered through ForMessage keep their group.
                if (consumer.Dispatch is not null)
                {
                    resolved = resolved with
                    {
                        Group = options.ResolveGroupName(
                            resolved.ResolvedHandlerId,
                            resolved.Lane == MessageLane.Bus ? resolved.ConsumerIdentity : resolved.MessageName
                        ),
                    };
                }

                var key = new ConsumerRegistrationKey(
                    resolved.MessageName,
                    resolved.Group,
                    resolved.Lane,
                    resolved.ConsumerType
                );

                var settings = new ConsumerRegistrationSettings(
                    resolved.Concurrency,
                    resolved.ResolvedHandlerId,
                    resolved.ConsumerIdentity,
                    resolved.MessageContractVersion,
                    resolved.InboxRetention,
                    ConsumerCircuitBreakerSettings.From(consumer.CircuitBreakerOverride),
                    resolved.ProviderConfigs,
                    resolved.EveryInstance,
                    resolved.FailurePolicy
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
                                + $"(group '{resolved.Group}', intent {resolved.Lane}) with conflicting settings: "
                                + $"the first registration has {existing.Describe()}; a later one has "
                                + $"{settings.Describe()}. Register the consumer once, or make every registration identical."
                        );
                    }

                    continue;
                }

                registeredKeys.Add(key, settings);
                consumers.Add((resolved, consumer.CircuitBreakerOverride));
            }
        }

        // Tuning never changes the identity, lane, or message a conflict check reads, so its errors are reported only
        // after registration: a conflicting declaration surfaces first.
        var errors = new List<string>();
        var tuned = ConsumerTuningApplier.Apply([.. consumers.Select(static x => x.Metadata)], controls, errors);
        for (var index = 0; index < consumers.Count; index++)
        {
            registry.Register(tuned[index]);
            _ApplyCircuitBreakerOverride(circuitBreakerRegistry, tuned[index], consumers[index].CircuitBreakerOverride);
        }

        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                $"Messaging tuning is invalid:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}"
            );
        }

        // Resolved against every registered identity, so a filter never makes a message unpublishable.
        var consumeFilter = MessagingConsumeFilter.Create(
            controls.ConsumeOnly,
            tuned.Select(static x => x.ConsumerIdentity)
        );

        registry.MarkMessageRegistrationDrainCompleted(consumeFilter);
    }

    private static void _ApplyCircuitBreakerOverride(
        ConsumerCircuitBreakerRegistry? circuitBreakerRegistry,
        ConsumerMetadata resolved,
        ConsumerCircuitBreakerOptions? circuitBreakerOverride
    )
    {
        if (circuitBreakerRegistry is null || circuitBreakerOverride is null)
        {
            return;
        }

        circuitBreakerRegistry.Register(CircuitBreakerKeys.For(resolved), circuitBreakerOverride);
    }

    private readonly record struct ConsumerRegistrationKey(
        string MessageName,
        string? Group,
        MessageLane Lane,
        Type ConsumerType
    )
    {
        // Message names are matched case-insensitively at dispatch, so the dedup key must treat
        // case-variant names as identical. Groups stay case-sensitive (Ordinal everywhere else).
        public bool Equals(ConsumerRegistrationKey other)
        {
            return Lane == other.Lane
                && ConsumerType == other.ConsumerType
                && string.Equals(MessageName, other.MessageName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Group, other.Group, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(MessageName),
                Group is null ? 0 : StringComparer.Ordinal.GetHashCode(Group),
                Lane,
                ConsumerType
            );
        }
    }

    private readonly struct ConsumerRegistrationSettings(
        byte concurrency,
        string resolvedHandlerId,
        string consumerIdentity,
        string messageContractVersion,
        TimeSpan inboxRetention,
        ConsumerCircuitBreakerSettings circuitBreaker,
        IReadOnlyDictionary<Type, object> providerConfigs,
        bool everyInstance,
        Type? failurePolicy
    ) : IEquatable<ConsumerRegistrationSettings>
    {
        private readonly bool _everyInstance = everyInstance;
        private readonly Type? _failurePolicy = failurePolicy;
        private readonly byte _concurrency = concurrency;
        private readonly string _resolvedHandlerId = resolvedHandlerId;
        private readonly string _consumerIdentity = consumerIdentity;
        private readonly string _messageContractVersion = messageContractVersion;
        private readonly TimeSpan _inboxRetention = inboxRetention;
        private readonly ConsumerCircuitBreakerSettings _circuitBreaker = circuitBreaker;
        private readonly IReadOnlyDictionary<Type, object> _providerConfigs = providerConfigs;

        public bool Equals(ConsumerRegistrationSettings other)
        {
            return _concurrency == other._concurrency
                && string.Equals(_resolvedHandlerId, other._resolvedHandlerId, StringComparison.Ordinal)
                && string.Equals(_consumerIdentity, other._consumerIdentity, StringComparison.Ordinal)
                && string.Equals(_messageContractVersion, other._messageContractVersion, StringComparison.Ordinal)
                && _inboxRetention == other._inboxRetention
                && _circuitBreaker == other._circuitBreaker
                && _everyInstance == other._everyInstance
                && _failurePolicy == other._failurePolicy
                && _ProviderConfigsEqual(_providerConfigs, other._providerConfigs);
        }

        public override bool Equals(object? obj)
        {
            return obj is ConsumerRegistrationSettings other && Equals(other);
        }

        // Names every compared setting so a conflict message shows both sides without the reader diffing sources.
        public string Describe()
        {
            var providerSettings = string.Join(
                ", ",
                _providerConfigs.Keys.Select(static type => type.Name).Order(StringComparer.Ordinal)
            );

            return $"concurrency {_concurrency}, consumer identity '{_consumerIdentity}', handler id '{_resolvedHandlerId}', "
                + $"contract version '{_messageContractVersion}', inbox retention {_inboxRetention}, "
                + $"circuit-breaker override {(_circuitBreaker.HasOverride ? "set" : "none")}, "
                + $"every instance {_everyInstance}, failure policy {_failurePolicy?.Name ?? "none"}, "
                + $"provider settings [{providerSettings}]";
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_concurrency);
            hash.Add(_resolvedHandlerId, StringComparer.Ordinal);
            hash.Add(_consumerIdentity, StringComparer.Ordinal);
            hash.Add(_messageContractVersion, StringComparer.Ordinal);
            hash.Add(_inboxRetention);
            hash.Add(_circuitBreaker);
            hash.Add(_everyInstance);
            hash.Add(_failurePolicy);

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
