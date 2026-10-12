// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Nats;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging;

/// <summary>Extension methods that attach NATS JetStream provider-specific options to a message contract or a tuned consumer.</summary>
[PublicAPI]
public static class NatsMessageBuilderExtensions
{
    /// <summary>
    /// Configures NATS JetStream options for <typeparamref name="TMessage"/> publish operations.
    /// </summary>
    /// <typeparam name="TMessage">The message type being registered.</typeparam>
    /// <param name="builder">The Bus route of the message contract.</param>
    /// <param name="configure">A delegate that configures the NATS options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IBusContractBuilder<TMessage> UseNats<TMessage>(
        this IBusContractBuilder<TMessage> builder,
        Action<NatsMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new NatsMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }

    /// <summary>Configures NATS JetStream options for <typeparamref name="TMessage"/> queue publish operations.</summary>
    public static IQueueContractBuilder<TMessage> UseNats<TMessage>(
        this IQueueContractBuilder<TMessage> builder,
        Action<NatsMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new NatsMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }

    /// <summary>
    /// Configures NATS JetStream consumer options for the declared consumer a <c>Tune</c> call targets.
    /// </summary>
    /// <param name="builder">The consumer tuning builder.</param>
    /// <param name="configure">A delegate that configures the NATS JetStream consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ConsumerTuningBuilder UseNats(
        this ConsumerTuningBuilder builder,
        Action<NatsConsumerConfigBuilder> configure
    )
    {
        _SetConsumerConfig(builder, configure);
        return builder;
    }

    private static void _SetConsumerConfig(
        IConsumerProviderConfigBuilder builder,
        Action<NatsConsumerConfigBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new NatsConsumerConfigBuilder();
        configure(configBuilder);
        builder.SetConsumerProviderConfig(configBuilder.Build());
    }
}

/// <summary>Fluent builder for NATS JetStream publish options applied to a single message type.</summary>
/// <typeparam name="TMessage">The message type being configured.</typeparam>
[PublicAPI]
public sealed class NatsMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _subjectShardSelector;
    private NatsExternalSubjectSpec<TMessage>? _externalSubject;

    /// <summary>
    /// Appends a dynamic shard token to the NATS subject, producing <c>{subject}.{shard}</c>.
    /// Use this to fan messages across multiple stream subjects for horizontal scaling.
    /// </summary>
    /// <param name="selector">
    /// A delegate that derives the shard token from the message instance.
    /// The token must be a single safe NATS subject token: it must be non-empty, at most 256 characters,
    /// and cannot contain <c>.</c>, <c>*</c>, <c>&gt;</c>, whitespace, or control characters. A non-null
    /// token that violates any of these rules causes an <see cref="InvalidOperationException"/> to be
    /// thrown at publish time rather than being silently dropped.
    /// Return <see langword="null"/> to skip sharding for this message.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public NatsMessageConfigBuilder<TMessage> SubjectShard(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _subjectShardSelector = selector;
        return this;
    }

    /// <summary>
    /// Binds this lane of the contract to a subject outside <c>headless.{lane}.{name}</c>, such as
    /// <c>acme.*.runs.*.result</c> published by a non-.NET service. Consumers of the lane read the filter, and a publish
    /// goes to the subject <see cref="NatsExternalSubjectBuilder{TMessage}.PublishTo"/> computes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filter must be covered by exactly one stream declared in <see cref="NatsMessagingOptions.Streams"/>, with
    /// <c>Own</c> or <c>Bind</c>: Headless never derives a stream for a foreign subject. Startup fails, naming the
    /// contract, the lane, and the filter, when no declared stream covers it, when it is not a valid subject filter,
    /// when it overlaps <c>headless.&gt;</c>, <c>_INBOX.&gt;</c>, <c>$JS.&gt;</c>, or <c>$SYS.&gt;</c>, when the lane
    /// also declares <see cref="SubjectShard"/>, or when it overlaps another contract's filter on the same lane.
    /// </para>
    /// <para>
    /// The subject a delivery arrives on names its contract: a delivery whose <c>headless-msg-name</c> header names
    /// another contract is a malformed envelope. An external subject does not imply <c>RawPayload()</c>; without it, a
    /// delivery still needs the Headless headers.
    /// </para>
    /// </remarks>
    /// <param name="filter">The NATS subject filter the lane consumes; <c>*</c> and a trailing <c>&gt;</c> are allowed.</param>
    /// <param name="configure">Optionally sets the publish subject selector and the tenant subject token.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="filter"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public NatsMessageConfigBuilder<TMessage> ExternalSubject(
        string filter,
        Action<NatsExternalSubjectBuilder<TMessage>>? configure = null
    )
    {
        Argument.IsNotNullOrWhiteSpace(filter);

        var builder = new NatsExternalSubjectBuilder<TMessage>();
        configure?.Invoke(builder);
        _externalSubject = new(filter, builder.PublishSelector, builder.TenantTokenIndex);
        return this;
    }

    internal NatsMessageConfig<TMessage> Build()
    {
        return new(_subjectShardSelector, _externalSubject);
    }
}

/// <summary>
/// Configures how a message contract lane bound to a subject outside <c>headless.{lane}.{name}</c> publishes and reads
/// its tenant. Created by <see cref="NatsMessageConfigBuilder{TMessage}.ExternalSubject"/>.
/// </summary>
/// <typeparam name="TMessage">The message type being configured.</typeparam>
[PublicAPI]
public sealed class NatsExternalSubjectBuilder<TMessage>
    where TMessage : class
{
    internal NatsExternalSubjectBuilder() { }

    internal Func<TMessage, string>? PublishSelector { get; private set; }

    internal int? TenantTokenIndex { get; private set; }

    /// <summary>
    /// Computes the subject each published message goes to. The subject is computed once, when the publish request is
    /// created, and stored with the message, so an outbox retry publishes to the same subject.
    /// </summary>
    /// <param name="selector">
    /// A delegate that derives the subject from the message instance. The subject must be literal (no <c>*</c> or
    /// <c>&gt;</c>, no empty token, no whitespace or control characters) and match the binding's filter; otherwise the
    /// publish throws an <see cref="InvalidOperationException"/> before anything is stored. Without a selector, a literal
    /// filter is the publish subject, and a wildcard filter makes the lane consume-only. A publish with a
    /// <see langword="null"/> payload has nothing to compute from: it goes to a literal filter, and on a wildcard filter
    /// it throws an <see cref="InvalidOperationException"/> before anything is stored.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public NatsExternalSubjectBuilder<TMessage> PublishTo(Func<TMessage, string> selector)
    {
        PublishSelector = Argument.IsNotNull(selector);
        return this;
    }

    /// <summary>
    /// Reads each delivery's tenant from one token of the subject it arrived on, for example token <c>1</c> of
    /// <c>acme.t1.runs.r9.result</c> is <c>t1</c>.
    /// </summary>
    /// <remarks>
    /// The subject is a stronger tenant source than a header: NATS permissions can restrict which subjects a producer
    /// may publish to, but not the header values it sends. A delivery whose subject lacks the token, whose token is longer
    /// than <see cref="MessageOptions.TenantIdMaxLength"/> characters, or whose tenant header differs from the token is a
    /// malformed envelope.
    /// </remarks>
    /// <param name="zeroBasedIndex">The zero-based index of the subject token that holds the tenant.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex"/> is negative.</exception>
    public NatsExternalSubjectBuilder<TMessage> TenantFromSubjectToken(int zeroBasedIndex)
    {
        TenantTokenIndex = Argument.IsPositiveOrZero(zeroBasedIndex);
        return this;
    }
}

/// <summary>
/// An external subject binding of one message type. Record equality compares the filter, the selector delegate, and the
/// token index, so identical declarations from one shared method merge.
/// </summary>
internal sealed record NatsExternalSubjectSpec<TMessage>(
    string Filter,
    Func<TMessage, string>? PublishSelector,
    int? TenantTokenIndex
)
    where TMessage : class
{
    public NatsExternalSubjectDeclaration Declaration { get; } =
        new(Filter, TenantTokenIndex, PublishSelector is not null);

    /// <summary>
    /// The subject a message is published to, computed when its publish request is created so the stored message keeps
    /// it across outbox retries.
    /// </summary>
    public string PublishSubject(TMessage message)
    {
        if (PublishSelector is null)
        {
            return NatsExternalSubjectRules.IsLiteral(Filter)
                ? Filter
                : throw new InvalidOperationException(
                    $"Message type '{typeof(TMessage).Name}' is bound to the NATS wildcard subject '{Filter}' without "
                        + "PublishTo, so it is consume-only. Declare PublishTo to publish it."
                );
        }

        return NatsExternalSubjectRules.ValidatePublishSubject(
            PublishSelector(message),
            Filter,
            $"message type '{typeof(TMessage).Name}'"
        );
    }
}

internal sealed class NatsMessageConfig<TMessage>(
    Func<TMessage, string?>? subjectShardSelector,
    NatsExternalSubjectSpec<TMessage>? externalSubject = null
) : IProviderHeaderContributions, IProviderMissingPayloadValidation, INatsMessageConfig
    where TMessage : class
{
    // A message contract merges with an identical redeclaration from another module, so two configs holding the same
    // selectors and external subject are equal and two different ones conflict.
    private readonly Func<TMessage, string?>? _selector = subjectShardSelector;
    private readonly NatsExternalSubjectSpec<TMessage>? _externalSubject = externalSubject;

    public NatsExternalSubjectDeclaration? ExternalSubject => _externalSubject?.Declaration;

    public bool HasSubjectShard => _selector is not null;

    public override bool Equals(object? obj) =>
        obj is NatsMessageConfig<TMessage> other
        && Equals(_selector, other._selector)
        && Equals(_externalSubject, other._externalSubject);

    public override int GetHashCode() => HashCode.Combine(_selector, _externalSubject);

    // A wildcard filter is not a subject, so a payload-less message has nowhere to go: PublishTo needs the payload, and
    // without PublishTo the lane is consume-only. Refusing here keeps it out of the outbox, where every send attempt
    // would fail. A literal filter is a subject, so a payload-less publish still goes there.
    public void ValidateMissingPayload(string messageName, MessageLane lane)
    {
        if (_externalSubject is not { } externalSubject || NatsExternalSubjectRules.IsLiteral(externalSubject.Filter))
        {
            return;
        }

        if (externalSubject.PublishSelector is null)
        {
            throw new InvalidOperationException(
                $"Message '{messageName}' on the {lane} lane is bound to the NATS wildcard subject "
                    + $"'{externalSubject.Filter}' without PublishTo, so it is consume-only. Declare PublishTo to "
                    + "publish it."
            );
        }

        throw new InvalidOperationException(
            $"Message '{messageName}' on the {lane} lane is bound to the NATS wildcard subject "
                + $"'{externalSubject.Filter}' and computes its publish subject with PublishTo, which needs the "
                + "payload. Publish it with a payload."
        );
    }

    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        _BuildContributions(subjectShardSelector, externalSubject);

    private static ProviderHeaderContribution[] _BuildContributions(
        Func<TMessage, string?>? subjectShardSelector,
        NatsExternalSubjectSpec<TMessage>? externalSubject
    )
    {
        var contributions = new List<ProviderHeaderContribution>(2);

        if (subjectShardSelector is not null)
        {
            contributions.Add(
                new ProviderHeaderContribution(
                    NatsMessagingHeaders.SubjectShard,
                    message => NatsSubjectShard.Validate(subjectShardSelector((TMessage)message))
                )
            );
        }

        if (externalSubject is not null)
        {
            contributions.Add(
                new ProviderHeaderContribution(
                    NatsMessagingHeaders.ExternalSubject,
                    message => externalSubject.PublishSubject((TMessage)message)
                )
            );
        }

        return [.. contributions];
    }
}

/// <summary>Fluent builder for NATS JetStream consumer options applied to a consumer registration.</summary>
/// <remarks>
/// Each <c>UseNats(...)</c> call replaces the NATS settings of an earlier one for the same consumer, so set every NATS
/// option of a consumer in one call. The durable name, filter subject, and delivery policy stay provider-owned.
/// </remarks>
[PublicAPI]
public sealed class NatsConsumerConfigBuilder
{
    private bool _isSharded;
    private TimeSpan? _ackWait;
    private int? _maxAckPending;
    private int? _maxDeliver;
    private TimeSpan? _inactiveThreshold;

    /// <summary>
    /// Declares that this consumer subscribes to sharded subjects (i.e. the producer uses
    /// <c>SubjectShard(...)</c>). When set, the consumer registers a <c>{subject}.&gt;</c>
    /// wildcard filter so that all shard tokens are received.
    /// </summary>
    /// <remarks>
    /// A consumer of a message whose contract this host declares with <c>SubjectShard(...)</c> filters on the shard
    /// wildcard without this call. Call it when the producer shards a message that this host declares without the
    /// shard: NATS delivers zero messages to a non-wildcard filter that matches no shard subject.
    /// </remarks>
    /// <returns>The same builder for chaining.</returns>
    public NatsConsumerConfigBuilder Sharded()
    {
        _isSharded = true;
        return this;
    }

    /// <summary>
    /// Sets how long JetStream waits for a delivery's acknowledgement before it redelivers the message. Defaults to
    /// <c>30 seconds</c>.
    /// </summary>
    /// <remarks>
    /// The consumer reports a delivery in progress every half <c>AckWait</c> until it settles, so a slow receive stage
    /// does not cause a redelivery; a shorter value detects a crashed consumer sooner at the cost of more progress
    /// signals.
    /// </remarks>
    /// <param name="ackWait">The acknowledgement wait.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ackWait"/> is not positive.</exception>
    public NatsConsumerConfigBuilder AckWait(TimeSpan ackWait)
    {
        _ackWait = Argument.IsPositive(ackWait);
        return this;
    }

    /// <summary>
    /// Sets how many deliveries may wait for acknowledgement at once across every instance of this consumer; JetStream
    /// stops delivering until one settles. Defaults to the server's limit (<c>1000</c>).
    /// </summary>
    /// <param name="maxAckPending">The most unacknowledged deliveries.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAckPending"/> is not positive.</exception>
    public NatsConsumerConfigBuilder MaxAckPending(int maxAckPending)
    {
        _maxAckPending = Argument.IsPositive(maxAckPending);
        return this;
    }

    /// <summary>
    /// Sets how many times JetStream delivers a message before it stops redelivering it. Defaults to no limit.
    /// </summary>
    /// <remarks>
    /// The transport rejects a delivery only before the inbox stores it: while the consumer's circuit breaker is open,
    /// or when the receive stage fails. Those rejections count toward the limit, and a message that reaches it is
    /// dropped without Headless ever storing it; JetStream publishes a <c>MAX_DELIVERIES</c> advisory for it. A stored
    /// message retries from storage and is not redelivered by JetStream. A rejected delivery is redelivered after a
    /// delay that doubles from about one second to 30 seconds, so a limit of 10 survives roughly three minutes of an
    /// open circuit.
    /// </remarks>
    /// <param name="maxDeliver">The most deliveries of one message.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDeliver"/> is not positive.</exception>
    public NatsConsumerConfigBuilder MaxDeliver(int maxDeliver)
    {
        _maxDeliver = Argument.IsPositive(maxDeliver);
        return this;
    }

    /// <summary>
    /// Lets JetStream delete this consumer's durables after they have had no pulling client for
    /// <paramref name="inactiveThreshold"/>, so the durables of a retired consumer identity do not pile up. Off by
    /// default: a durable lives until it is deleted.
    /// </summary>
    /// <remarks>
    /// A Bus durable starts at the messages published after it is created (<c>DeliverPolicy.New</c>). Once JetStream
    /// deletes it during a downtime longer than the threshold, the next start creates a new durable, and every Bus
    /// message published while the consumer was down is lost to it. Set the threshold well above any outage or deploy
    /// the consumer must survive. A Queue durable starts at the first stored message, so a deleted one loses nothing
    /// the stream still holds.
    /// </remarks>
    /// <param name="inactiveThreshold">How long a durable may have no pulling client.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="inactiveThreshold"/> is not positive.</exception>
    public NatsConsumerConfigBuilder InactiveThreshold(TimeSpan inactiveThreshold)
    {
        _inactiveThreshold = Argument.IsPositive(inactiveThreshold);
        return this;
    }

    internal NatsConsumerConfig Build()
    {
        return new(_isSharded, _ackWait, _maxAckPending, _maxDeliver, _inactiveThreshold);
    }
}

internal sealed record NatsConsumerConfig(
    bool IsSharded,
    TimeSpan? AckWait = null,
    int? MaxAckPending = null,
    int? MaxDeliver = null,
    TimeSpan? InactiveThreshold = null
)
{
    /// <summary>Writes the limits this consumer sets onto <paramref name="config"/>, leaving the rest as they are.</summary>
    public void ApplyTo(ConsumerConfig config)
    {
        if (AckWait is { } ackWait)
        {
            config.AckWait = ackWait;
        }

        if (MaxAckPending is { } maxAckPending)
        {
            config.MaxAckPending = maxAckPending;
        }

        if (MaxDeliver is { } maxDeliver)
        {
            config.MaxDeliver = maxDeliver;
        }

        if (InactiveThreshold is { } inactiveThreshold)
        {
            config.InactiveThreshold = inactiveThreshold;
        }
    }
}
