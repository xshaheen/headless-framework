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
    /// This method configures messaging infrastructure. Consumers declare themselves with
    /// <see cref="BusConsumerAttribute"/> or <see cref="QueueConsumerAttribute"/> and register through their assembly's
    /// generated <see cref="IMessagingModule"/>, added here or from a module's <c>ConfigureMessaging</c> contribution.
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
    ///     setup.AddModule&lt;Orders.MessagingModule&gt;();
    ///     setup.Tune(OrderPlacedHandler.Identity, consumer => consumer.Concurrency(5));
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

        var options = new MessagingOptions();
        var setup = new MessagingSetupBuilder(services, options);

        configure(setup);

        return _RegisterCoreMessagingServices(services, setup);
    }

    private static MessagingBuilder _RegisterCoreMessagingServices(
        IServiceCollection services,
        MessagingSetupBuilder setup
    )
    {
        var options = setup.Options;

        if (setup.OutboxBuilders.Exists(static builder => !builder.IsConfigured))
        {
            throw new InvalidOperationException(
                "AddOutbox() was called without a storage. Chain UseEntityFramework<TContext>(), UsePostgreSql(...), or UseSqlServer(...) on it."
            );
        }

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
        // The registry folds every declaration recorded in the collection the first time anything resolves it, so a
        // contribution counts whether it came before or after this call, and a publish before startup already sees the
        // declared names.
        services.TryAddSingleton(static sp => BuildConsumerRegistry(sp));
        services.TryAddSingleton<IConsumerRegistry>(sp => sp.GetRequiredService<ConsumerRegistry>());
        services.TryAddSingleton<IMessageMetadataRegistry>(sp =>
        {
            var registry = sp.GetRequiredService<ConsumerRegistry>();
            return new MessageMetadataRegistry(
                registry.DeclaredRoutes,
                registry,
                sp.GetRequiredService<IOptions<MessagingOptions>>()
            );
        });
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
        var includeTenantIdInMetricTags = setup.Instrumentation.IncludeTenantIdInMetricTags;
        services.TryAddSingleton(sp => new InboxMetricPolicy(
            includeTenantIdInMetricTags
                ? (
                    sp.GetService<IOptions<TenantTelemetryOptions>>()?.Value ?? new TenantTelemetryOptions()
                ).AttributeName
                : null
        ));
        services.TryAddSingleton(sp => new MessagingTelemetry(
            messagingEnrichers,
            sp.GetService<ILogger<MessagingTelemetry>>(),
            sp.GetService<IOptions<TenantTelemetryOptions>>()?.Value
        ));

        // The primary storage plus every AddOutbox() registration. Resolving it validates them together, so the
        // bootstrapper resolves it before storage initialization and a misconfiguration fails host startup.
        services.TryAddSingleton(MessagingOutboxes.Create);
        services.TryAddSingleton<OutboxMessageWriter>();
        services.TryAddSingleton<IMessageRevoker, MessageRevoker>();
        services.TryAddSingleton<IRuntimeConsumerRegistry, RuntimeConsumerRegistry>();
        services.TryAddSingleton<IRuntimeSubscriber, RuntimeSubscriber>();
        services.TryAddSingleton<IConsumerHostOwnership, ConsumerHostOwnership>();

        services.TryAddSingleton<IConsumerServiceSelector, ConsumerServiceSelector>();
        services.TryAddSingleton<IConsumeMiddlewarePipeline, ConsumeMiddlewarePipeline>();
        // Singleton-with-internal-AsyncScope, mirroring IConsumeMiddlewarePipeline above. Both publishers
        // it serves are Singleton too, so a Scoped pipeline would be a captive dependency. Per-publish
        // scope is created inside ExecuteAsync so scoped publish middleware instances resolve fresh per call.
        services.TryAddSingleton<IPublishMiddlewarePipeline, PublishMiddlewarePipeline>();
        services.TryAddSingleton<ISubscribeInvoker, SubscribeInvoker>();
        services.TryAddSingleton<MethodMatcherCache>();

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
        services.TryAddSingleton<OutboxInitializationProcessor>();

        //Sender
        services.TryAddSingleton<IMessageSender, MessageSender>();

        // Resolved only by a responder host, which bootstrap admits only on a transport that registers IReplyTransport.
        services.TryAddSingleton<ReplySender>();

        services.TryAddSingleton<ISerializer, JsonUtf8Serializer>();

        // One id per host: every-instance subscriptions name their per-process broker object after it.
        services.TryAddSingleton<MessagingInstanceId>();

        // Warning: IPublishMessageSender need to inject at extension project.
        services.TryAddSingleton<ISubscribeExecutor, SubscribeExecutor>();

        services.TryAddSingleton<IDispatcher, Dispatcher>();

        // Circuit breaker
        services.AddMetrics();
        services.TryAddSingleton(sp => sp.GetRequiredService<ConsumerRegistry>().CircuitBreakers);
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
                () => sp.GetService<MessagingOutboxes>(),
                () => sp.GetService<OutboxMessageWriter>(),
                sp.GetService<MessagingTelemetry>(),
                options.TransportPublishTimeout,
                sp.GetRequiredService<IOptions<MessagingOptions>>().Value.DefaultDeliveryMode,
                sp.GetRequiredService<ConsumerRegistry>().DeclaredRoutes
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

        return new MessagingBuilder(services);
    }

    /// <summary>
    /// Builds and freezes the host's consumer registry from everything recorded in the service collection: the message
    /// declarations, in registration order, then every added generated module, the <c>Tune</c> calls, and the
    /// <c>ConsumeOnly</c> entries. The container calls it once per provider, the first time anything resolves the
    /// registry, so a <see cref="MessagingContributionExtensions.ConfigureMessaging"/> contribution counts whether it ran
    /// before or after <see cref="AddHeadlessMessaging"/>.
    /// </summary>
    internal static ConsumerRegistry BuildConsumerRegistry(IServiceProvider provider) =>
        BuildConsumerRegistry(provider, new ConsumerRegistry());

    /// <summary>
    /// Folds the host's registrations into <paramref name="registry"/>, which may already hold consumers that no
    /// declaration can express, such as a wildcard subscription, then freezes it.
    /// </summary>
    internal static ConsumerRegistry BuildConsumerRegistry(IServiceProvider provider, ConsumerRegistry registry)
    {
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        var declarations = MessageDeclarationFold.Create(provider.GetServices<MessageDeclaration>());

        foreach (var mapping in declarations.NameMappings)
        {
            registry.RegisterMessageName(mapping.MessageType, mapping.Name);
        }

        foreach (var route in declarations.Routes)
        {
            registry.RegisterMessageName(route.MessageType, route.Lane, route.MessageName!);
        }

        // Generated modules register after the contracts, so their consumers read the contract versions those contracts
        // declare.
        var registrations = new List<MessageRegistration>(declarations.Routes);
        registrations.AddRange(
            CreateModuleRegistrations(provider.GetServices<MessagingModuleContribution>(), declarations.Routes)
        );

        var controls = new MessagingHostControls(
            [.. provider.GetServices<MessagingTuningContribution>().Select(static x => x.Tuning)],
            [.. provider.GetServices<MessagingConsumeOnlyContribution>().SelectMany(static x => x.Entries)],
            provider.GetService<IConfiguration>()
        );

        var consumeFilter = _RegisterConsumers(registrations, options, registry, controls);
        registry.Complete(declarations, consumeFilter);

        return registry;
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
                    consumer.Identity,
                    consumer.Dispatch
                )
                {
                    EveryInstance = consumer.EveryInstance,
                    DeclaringModule = consumer.Source,
                    OnSubscriptionEstablished = consumer.OnSubscriptionEstablished,
                    ResponseType = consumer.ResponseType,
                },
                contractVersions.GetValueOrDefault(
                    (consumer.MessageType, consumer.Lane),
                    MessageOptions.InitialContractVersion
                )
            )
        );
    }

    /// <summary>
    /// Registers the host's consumers from the folded registrations, then, in order: detects identity, Queue, and route
    /// conflicts; applies <c>Tune</c> calls and <c>Headless:Messaging:Consumers:{identity}</c> configuration; and
    /// resolves the <c>ConsumeOnly</c> filter it returns.
    /// </summary>
    private static MessagingConsumeFilter _RegisterConsumers(
        IReadOnlyCollection<MessageRegistration> registrations,
        MessagingOptions options,
        ConsumerRegistry registry,
        MessagingHostControls controls
    )
    {
        var registeredKeys =
            new Dictionary<ConsumerRegistrationKey, (ConsumerRegistrationSettings Settings, int Index)>();
        var consumers = new List<ConsumerMetadata>();

        foreach (var registration in registrations)
        {
            foreach (var consumer in registration.Consumers)
            {
                var resolved = options.CreateConsumerMetadata(
                    consumer.ConsumerType,
                    registration.MessageType,
                    registry.TryGetRawMessageName(
                        registration.MessageType,
                        registration.Lane,
                        out var mappedMessageName
                    )
                        ? mappedMessageName
                        : null,
                    consumer.ConsumerIdentity,
                    registration.ContractVersion,
                    registration.Lane
                ) with
                {
                    EveryInstance = consumer.EveryInstance,
                    Dispatch = consumer.Dispatch,
                    OnSubscriptionEstablished = consumer.OnSubscriptionEstablished,
                    DeclaringModule = consumer.DeclaringModule,
                    ResponseType = consumer.ResponseType,
                };

                var key = new ConsumerRegistrationKey(resolved.MessageName, resolved.Lane, resolved.ConsumerType);
                var settings = new ConsumerRegistrationSettings(
                    resolved.ConsumerIdentity,
                    resolved.MessageContractVersion,
                    resolved.EveryInstance,
                    resolved.ResponseType
                );

                if (registeredKeys.TryGetValue(key, out var existing))
                {
                    // A hand-written module may redeclare a generated consumer. An identical redeclaration merges; a
                    // different one would silently lose settings, so it fails naming both.
                    if (existing.Settings != settings)
                    {
                        throw new InvalidOperationException(
                            $"Consumer {resolved.ConsumerType.FullName ?? resolved.ConsumerType.Name} is declared more "
                                + $"than once for message name '{resolved.MessageName}' on lane {resolved.Lane} with "
                                + $"conflicting settings: {existing.Settings} and {settings}. Declare the consumer once, "
                                + "or make every declaration identical."
                        );
                    }

                    // A hand-written redeclaration carries no subscription hook, so whichever declaration came first,
                    // the merged consumer keeps the generated one.
                    if (
                        consumers[existing.Index].OnSubscriptionEstablished is null
                        && resolved.OnSubscriptionEstablished is { } hook
                    )
                    {
                        consumers[existing.Index] = consumers[existing.Index] with { OnSubscriptionEstablished = hook };
                    }

                    continue;
                }

                registeredKeys.Add(key, (settings, consumers.Count));
                consumers.Add(resolved);
            }
        }

        // Tuning never changes the identity, lane, or message a conflict check reads, so its errors are reported only
        // after registration: a conflicting declaration surfaces first.
        var errors = new List<string>();
        var tuned = ConsumerTuningApplier.Apply(consumers, controls, errors);
        var circuitBreakerKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var consumer in tuned)
        {
            registry.Register(consumer);

            // A Bus identity covering several messages has one entry per message but one circuit, so its override is
            // registered once.
            if (
                consumer.CircuitBreakerOverride is { } circuitBreaker
                && circuitBreakerKeys.Add(CircuitBreakerKeys.For(consumer))
            )
            {
                registry.CircuitBreakers.Register(CircuitBreakerKeys.For(consumer), circuitBreaker);
            }
        }

        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                $"Messaging tuning is invalid:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}"
            );
        }

        // Resolved against every registered identity, so a filter never makes a message unpublishable.
        return MessagingConsumeFilter.Create(controls.ConsumeOnly, tuned);
    }

    private readonly record struct ConsumerRegistrationKey(string MessageName, MessageLane Lane, Type ConsumerType)
    {
        // Message names are matched case-insensitively at dispatch, so the dedup key must treat case-variant names as
        // identical.
        public bool Equals(ConsumerRegistrationKey other)
        {
            return Lane == other.Lane
                && ConsumerType == other.ConsumerType
                && string.Equals(MessageName, other.MessageName, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(MessageName), Lane, ConsumerType);
        }
    }

    // The response type is a setting: a redeclaration that answers with another type, or not at all, would change what the
    // caller receives.
    private readonly record struct ConsumerRegistrationSettings(
        string ConsumerIdentity,
        string MessageContractVersion,
        bool EveryInstance,
        Type? ResponseType
    );
}
