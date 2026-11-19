// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.RequestReply;
using Headless.Reliability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

/// <summary>
/// Setup-time builder passed to the <c>AddHeadlessMessaging</c> delegate.
/// Carries the <see cref="MessagingOptions"/> being configured plus the setup-only
/// state (<see cref="IServiceCollection"/> and the options-extension list) that must not leak into the runtime
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> instance.
/// </summary>
/// <remarks>
/// Splitting these concerns prevents the <c>CopyTo</c> trap where adding a new
/// <see cref="MessagingOptions"/> property silently drops out of the DI-resolved
/// instance if the maintainer forgets to update <c>CopyTo</c>.
/// </remarks>
[PublicAPI]
public sealed partial class MessagingSetupBuilder : IMessagingBuilder
{
    /// <summary>
    /// The name of the primary storage among the host's outbox storages. <see cref="AddOutbox"/> refuses it, so every
    /// store a host registers has a distinct name.
    /// </summary>
    public const string PrimaryOutboxName = "primary";

    /// <summary>
    /// The name of the default transport, the one <c>setup.Use…</c> registers. <see cref="AddTransport"/> refuses it, and
    /// <c>Tune(identity, c =&gt; c.Transport(DefaultTransportName))</c> binds a consumer back to the default transport.
    /// </summary>
    public const string DefaultTransportName = "default";

    /// <summary>The longest transport name <see cref="AddTransport"/> accepts, the width of the stored column.</summary>
    public const int TransportNameMaxLength = 64;

    private readonly List<string> _consumeOnly = [];

    internal MessagingSetupBuilder(IServiceCollection services, MessagingOptions options)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(options);

        Services = services;
        Options = options;
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
    /// <c>Headless.Messaging</c>. Subscribing an OpenTelemetry exporter (or any
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
    /// Adds an outbox storage named <paramref name="name"/> for another database, next to the primary storage, so a
    /// unit of work begun on that database can publish atomically through <c>unit.Outbox</c>.
    /// </summary>
    /// <param name="name">
    /// The store's name, unique within the host: letters, digits, <c>-</c>, and <c>_</c>, compared case-insensitively.
    /// <see cref="PrimaryOutboxName"/> is reserved for the primary storage.
    /// </param>
    /// <returns>A builder that accepts only the storage of the new outbox.</returns>
    /// <remarks>
    /// <para>
    /// The additional outbox holds published rows and relays them under its own lease, so an outage of its
    /// database does not stall the relay of the others. The inbox, retry state, and the dashboard stay on the
    /// primary storage, which must be relational.
    /// </para>
    /// <para>
    /// The name identifies the store in logs, configuration errors, health checks, and the host manifest, and the
    /// dashboard keys message ids, change cursors, operation scopes, and command routing on it. It is chosen rather
    /// than derived from the registration's position, because a position shifts when a registration is added or
    /// reordered, while a chosen name stays bound to its database across redeploys.
    /// </para>
    /// <para>
    /// <c>unit.Outbox</c> writes to the outbox whose database matches the unit's connection. Registering two outboxes,
    /// the primary included, that resolve to the same database fails host startup.
    /// </para>
    /// <code>
    /// services.AddHeadlessMessaging(setup =>
    /// {
    ///     setup.UsePostgreSqlEntityFramework&lt;OrdersDb&gt;();
    ///     setup.AddOutbox("billing").UsePostgreSqlEntityFramework&lt;BillingDb&gt;();
    /// });
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> holds a character other than a letter, digit, <c>-</c>, or <c>_</c>, is
    /// <see cref="PrimaryOutboxName"/>, or names an outbox this host already registered.
    /// </exception>
    public OutboxStorageBuilder AddOutbox(string name)
    {
        Argument.IsNotNull(name);
        Argument.Matches(
            name,
            _OutboxNamePattern,
            $"Outbox name '{name}' must contain only letters, digits, '-', and '_'."
        );

        if (string.Equals(name, PrimaryOutboxName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Outbox name '{name}' is reserved for the primary storage. Choose another name.",
                nameof(name)
            );
        }

        if (OutboxBuilders.Exists(builder => string.Equals(builder.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"An outbox named '{name}' is already registered on this host. Give each outbox its own name.",
                nameof(name)
            );
        }

        var builder = new OutboxStorageBuilder(this, name, OutboxBuilders.Count + 1);
        OutboxBuilders.Add(builder);

        return builder;
    }

    // \z, not $: in .NET $ also matches before a final newline, which would admit "billing\n" beside "billing".
    [GeneratedRegex(@"^[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _OutboxNamePattern { get; }

    /// <summary>
    /// Adds a named transport, a second broker beside the default transport, for example
    /// <c>setup.AddTransport("partner").UseRabbitMq(...)</c>.
    /// </summary>
    /// <param name="name">
    /// The transport's name, unique within the host: letters, digits, <c>-</c>, and <c>_</c>, at most
    /// <see cref="TransportNameMaxLength"/> characters, compared case-insensitively. <see cref="DefaultTransportName"/>
    /// is reserved for the default transport.
    /// </param>
    /// <returns>A builder that accepts only the provider of the new transport.</returns>
    /// <remarks>
    /// <para>
    /// The named transport shares the host's storage, inbox, retry relay, failure policies, and dashboard. Its own are the
    /// broker connection, the consumer clients, the topology, the capability checks, and the broker health check
    /// (<c>messaging-broker-{name}</c>), so an outage of one broker does not stop the other.
    /// </para>
    /// <para>
    /// Publish through it with the keyed <see cref="IBus"/> and <see cref="IQueue"/>
    /// (<c>[FromKeyedServices("partner")] IBus bus</c>), or inside a unit of work with
    /// <see cref="OutboxOptions.TransportName"/> on <c>unit.Outbox</c>. Bind a consumer to it with
    /// <c>Tune(identity, c =&gt; c.Transport("partner"))</c> or the
    /// <c>Headless:Messaging:Consumers:{identity}:Transport</c> setting. Each durable row records the transport it was
    /// published through, so the relay sends it through the same broker after a restart.
    /// </para>
    /// <para>Request/reply runs on the default transport only.</para>
    /// <code>
    /// services.AddHeadlessMessaging(setup =>
    /// {
    ///     setup.UsePostgreSql(connectionString);
    ///     setup.UseRabbitMq(internalBroker => internalBroker.HostName = "rabbit-internal");
    ///     setup.AddTransport("partner").UseRabbitMq(partner => partner.HostName = "rabbit-partner");
    ///     setup.Tune("partner.order-sync", consumer => consumer.Transport("partner"));
    /// });
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a valid transport name, is <see cref="DefaultTransportName"/>, or names a transport
    /// this host already registered.
    /// </exception>
    public MessagingTransportBuilder AddTransport(string name)
    {
        ValidateTransportName(name);

        if (IsDefaultTransport(name))
        {
            throw new ArgumentException(
                $"Transport name '{name}' is reserved for the default transport. Choose another name.",
                nameof(name)
            );
        }

        if (TransportBuilders.Exists(builder => string.Equals(builder.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"A transport named '{name}' is already registered on this host. Give each transport its own name.",
                nameof(name)
            );
        }

        var builder = new MessagingTransportBuilder(this, name);
        TransportBuilders.Add(builder);
        Services.AddSingleton(new MessagingTransportRegistration(name));

        return builder;
    }

    internal List<MessagingTransportBuilder> TransportBuilders { get; } = [];

    /// <summary>Whether <paramref name="name"/> is a well-formed transport name.</summary>
    internal static bool IsValidTransportName([NotNullWhen(true)] string? name) =>
        name is { Length: > 0 and <= TransportNameMaxLength } && _OutboxNamePattern.IsMatch(name);

    /// <summary>Whether <paramref name="name"/> names the default transport.</summary>
    internal static bool IsDefaultTransport(string? name) =>
        name is null || string.Equals(name, DefaultTransportName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <paramref name="name"/> when it is a well-formed transport name, and throws otherwise.</summary>
    internal static string ValidateTransportName(
        string name,
        [CallerArgumentExpression(nameof(name))] string? paramName = null
    )
    {
        Argument.IsNotNull(name, paramName: paramName);

        if (!IsValidTransportName(name))
        {
            throw new ArgumentException(
                $"Transport name '{name}' must contain only letters, digits, '-', and '_', and be at most "
                    + $"{TransportNameMaxLength} characters.",
                paramName
            );
        }

        return name;
    }

    /// <summary>
    /// Enables this host to send requests with <see cref="IRequestClient"/> and await their replies, for example
    /// <c>setup.AddRequestReply(requests =&gt; requests.DefaultTimeout = TimeSpan.FromSeconds(10))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It registers <see cref="IRequestClient"/> and a reply listener that opens when messaging starts, before any
    /// consumer, and closes when it stops. A host that never calls it registers no client and opens no reply channel;
    /// a host that only answers requests does not need it.
    /// </para>
    /// <para>
    /// The transport must support request/reply, or messaging fails to start naming the provider. Calling this more than
    /// once is harmless; each <paramref name="configure"/> applies to the same <see cref="MessagingOptions.RequestReply"/>.
    /// </para>
    /// </remarks>
    /// <param name="configure">Optionally changes the request/reply settings, such as the default timeout.</param>
    /// <returns>This builder, for chaining.</returns>
    public MessagingSetupBuilder AddRequestReply(Action<RequestReplyOptions>? configure = null)
    {
        configure?.Invoke(Options.RequestReply);

        Services.TryAddSingleton<RequestReplyMarkerService>();
        Services.TryAddSingleton(static sp => new PendingRequests(
            sp.GetRequiredService<IOptions<MessagingOptions>>().Value.RequestReply.MaxPendingRequests
        ));
        Services.TryAddSingleton<ReplyDispatcher>();
        Services.TryAddSingleton<ReplyListenerHost>();
        Services.TryAddSingleton<IRequestClient, RequestClient>();

        return this;
    }

    /// <summary>
    /// Tunes the deployment settings of one declared consumer on this host, for example
    /// <c>Tune("billing.invoice-projection", consumer =&gt; consumer.Concurrency(16))</c>. Equivalent to the same call on
    /// <c>services.ConfigureMessaging(...)</c>.
    /// </summary>
    /// <remarks>
    /// The identity is checked when messaging starts: an identity no registered consumer declares fails startup.
    /// <c>Headless:Messaging:Consumers:{identity}</c> configuration (<c>Concurrency</c>, <c>InboxRetention</c>,
    /// <c>CircuitBreaker</c>, and <c>FailurePolicy</c>) applies after every
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
    /// Sets the failure policy of every competing consumer on this host that neither declares one on its attribute nor
    /// has one tuned, and of every competing runtime subscription. Without this call such a consumer retries 2 times
    /// at once, then 5 times after a delay that starts at 30 seconds and doubles up to 15 minutes.
    /// </summary>
    /// <remarks>
    /// <c>Headless:Messaging:Consumers:{identity}:FailurePolicy</c> configuration still adjusts the retry counts and
    /// delays of a consumer that uses the default. A later call replaces an earlier one.
    /// </remarks>
    /// <typeparam name="TPolicy">The policy type; it is built once, during this call.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException">The policy's retry counts or delays are out of range.</exception>
    public MessagingSetupBuilder DefaultFailurePolicy<TPolicy>()
        where TPolicy : FailurePolicy, new()
    {
        Options.DefaultFailurePolicy = FailurePolicyDefinition.Create<TPolicy>();
        return this;
    }

    /// <summary>
    /// Sets the host's default consumer failure policy inline, for example
    /// <c>DefaultFailurePolicy(p =&gt; p.Immediate(1).Delayed(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)))</c>.
    /// See <see cref="DefaultFailurePolicy{TPolicy}"/>.
    /// </summary>
    /// <param name="configure">Describes the policy; it runs once, synchronously, during this call.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The policy's retry counts or delays are out of range.</exception>
    public MessagingSetupBuilder DefaultFailurePolicy([InstantHandle] Action<FailurePolicyBuilder> configure)
    {
        Options.DefaultFailurePolicy = FailurePolicyDefinition.Create(configure);
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

    internal IServiceCollection Services { get; }

    internal IList<IMessagesOptionsExtension> Extensions { get; } = [];

    internal List<OutboxStorageBuilder> OutboxBuilders { get; } = [];

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
        MessagingOptions.ValidateMessageName(messageName);

        // Folded with every other declaration when the host's consumer registry freezes, where a second mapping of the
        // type to a different name fails.
        Services.AddSingleton<MessageDeclaration>(new MessageNameMappingDeclaration(typeof(TMessage), messageName));
        return this;
    }

    /// <inheritdoc />
    public IMessagingBuilder UseConventions(Action<MessagingConventions> configure)
    {
        Argument.IsNotNull(configure);

        configure(Options.Conventions);
        return this;
    }
}
