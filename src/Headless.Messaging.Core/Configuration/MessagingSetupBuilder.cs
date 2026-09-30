// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Registration;
using Microsoft.Extensions.Configuration;
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
    private readonly List<string> _consumeOnly = [];

    internal MessagingSetupBuilder(IServiceCollection services, MessagingOptions options, ConsumerRegistry registry)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(options);
        Argument.IsNotNull(registry);

        Services = services;
        Options = options;
        Registry = registry;
        var sink = new MessageRegistrationSink(services, registry);
        Bus = new BusRegistrationBuilder(sink);
        Queue = new QueueRegistrationBuilder(sink);
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

    /// <summary>
    /// Binds the shared <see cref="MessagingStorageOptions"/>, which owns the database naming used by every
    /// storage provider, from <paramref name="configuration"/>.
    /// </summary>
    /// <param name="configuration">
    /// The configuration section to bind, normally <c>Headless:Messaging:Storage</c>. The section's keys map
    /// to the option's properties, so the schema comes from its <c>Schema</c> key.
    /// </param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public MessagingSetupBuilder ConfigureStorage(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        Services.Configure<MessagingStorageOptions>(configuration);

        return this;
    }

    /// <summary>
    /// Applies <paramref name="configure"/> to the shared <see cref="MessagingStorageOptions"/>, which owns
    /// the database naming used by every storage provider.
    /// </summary>
    /// <param name="configure">A delegate that mutates the storage options.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public MessagingSetupBuilder ConfigureStorage(Action<MessagingStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure(configure);

        return this;
    }

    /// <summary>
    /// Adds one assembly's generated consumers, for example <c>AddModule&lt;Billing.MessagingModule&gt;()</c>. Equivalent
    /// to the same call on <c>services.ConfigureMessaging(...)</c>; adding a module more than once is harmless.
    /// </summary>
    /// <remarks>
    /// The Messaging source generator emits one <see cref="IMessagingModule"/> per assembly that declares
    /// <see cref="BusConsumerAttribute"/> or <see cref="QueueConsumerAttribute"/> consumers. Its consumers register when
    /// messaging starts.
    /// </remarks>
    /// <typeparam name="TModule">The generated <see cref="IMessagingModule"/> of the assembly.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public MessagingSetupBuilder AddModule<TModule>()
        where TModule : IMessagingModule
    {
        Services.AddMessagingModuleContribution<TModule>();
        return this;
    }

    /// <summary>
    /// Tunes the deployment settings of one declared consumer on this host, for example
    /// <c>Tune("billing.invoice-projection", consumer =&gt; consumer.Concurrency(16))</c>. Equivalent to the same call on
    /// <c>services.ConfigureMessaging(...)</c>.
    /// </summary>
    /// <remarks>
    /// The identity is checked when messaging starts: an identity no registered consumer declares fails startup.
    /// <c>Headless:Messaging:Consumers:{identity}</c> configuration (<c>Concurrency</c>) applies after every
    /// <c>Tune</c> call. <paramref name="configure"/> runs once, synchronously, during this call.
    /// </remarks>
    /// <param name="identity">The consumer's identity.</param>
    /// <param name="configure">Changes the consumer's deployment settings.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="identity"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public MessagingSetupBuilder Tune(string identity, [InstantHandle] Action<ConsumerTuningBuilder> configure)
    {
        Services.AddConsumerTuning(identity, configure);
        return this;
    }

    /// <summary>
    /// Limits which registered consumers this host consumes with. Each entry is an exact consumer identity, such as
    /// <c>billing.invoice-projection</c>, or an <c>owner.*</c> pattern, such as <c>orders.*</c>, that matches every
    /// consumer whose identity starts with that owner segment. Calls accumulate.
    /// </summary>
    /// <remarks>
    /// Consumers outside the filter stay registered: this host still publishes their messages and describes them, and
    /// another host without the filter consumes them. Without any <c>ConsumeOnly</c> call the host consumes with every
    /// consumer. Every-instance consumers are never filtered: each host runs them because they keep per-process state.
    /// An entry that matches no registered consumer, or only every-instance consumers, fails startup.
    /// </remarks>
    /// <param name="identities">Exact consumer identities or <c>owner.*</c> patterns.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identities"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="identities"/> is empty, or an entry is blank or misplaces <c>*</c>.
    /// </exception>
    public MessagingSetupBuilder ConsumeOnly(params string[] identities)
    {
        Argument.IsNotNullOrEmpty(identities);
        var validated = identities.Select(MessagingConsumeFilter.ValidateEntry).ToArray();
        _consumeOnly.AddRange(validated);
        return this;
    }

    /// <summary>The <c>ConsumeOnly</c> entries authored so far, snapshotted when <c>AddHeadlessMessaging</c> returns.</summary>
    internal string[] FreezeConsumeOnly() => [.. _consumeOnly];

    /// <summary>Gets the structural registration root for Bus consumers.</summary>
    public IBusRegistrationBuilder Bus { get; }

    /// <summary>Gets the structural registration root for Queue consumers.</summary>
    public IQueueRegistrationBuilder Queue { get; }

    internal IServiceCollection Services { get; }

    internal ConsumerRegistry Registry { get; }

    internal ConsumerCircuitBreakerRegistry CircuitBreakerRegistry { get; } = new();

    internal IList<IMessagesOptionsExtension> Extensions { get; } = [];

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
        MessageLane lane,
        string consumerIdentity,
        string messageContractVersion
    )
    {
        var metadata = Options.CreateConsumerMetadata(
            consumerType,
            messageType,
            messageName,
            Registry.TryGetRawMessageName(messageType, lane, out var mappedMessageName) ? mappedMessageName : null,
            group,
            concurrency,
            consumerIdentity: consumerIdentity,
            messageContractVersion: messageContractVersion,
            lane: lane
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
