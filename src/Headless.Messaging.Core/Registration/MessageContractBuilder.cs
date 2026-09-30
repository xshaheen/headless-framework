// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Messaging.Configuration;

namespace Headless.Messaging.Registration;

/// <summary>
/// Configures one message contract, declared once for both lanes with <c>Message&lt;T&gt;(name, version)</c>.
/// </summary>
/// <remarks>
/// The contract belongs to the message schema rather than to a lane: publishing on the Bus and sending on the Queue both
/// resolve the message type to the same name and version. Settings that only make sense for one lane's route are chained
/// with <see cref="OnBus"/> and <see cref="OnQueue"/>, and they touch only that route. The message type itself needs no
/// attribute and no reference to Headless.
/// </remarks>
/// <typeparam name="TMessage">The message type the contract describes.</typeparam>
[PublicAPI]
public interface IMessageContractBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Derives a correlation identifier from the outgoing payload on both lanes.</summary>
    /// <param name="selector">Reads the correlation identifier from the message.</param>
    /// <returns>This builder, for chaining.</returns>
    IMessageContractBuilder<TMessage> CorrelateBy(Func<TMessage, string?> selector);

    /// <summary>Configures settings of the message's Bus route only.</summary>
    /// <param name="configure">Changes the Bus route settings.</param>
    /// <returns>This builder, for chaining.</returns>
    IMessageContractBuilder<TMessage> OnBus([InstantHandle] Action<IBusContractBuilder<TMessage>> configure);

    /// <summary>Configures settings of the message's Queue route only.</summary>
    /// <param name="configure">Changes the Queue route settings.</param>
    /// <returns>This builder, for chaining.</returns>
    IMessageContractBuilder<TMessage> OnQueue([InstantHandle] Action<IQueueContractBuilder<TMessage>> configure);
}

/// <summary>Configures the Bus route of one message contract.</summary>
/// <typeparam name="TMessage">The message type the contract describes.</typeparam>
[PublicAPI]
public interface IBusContractBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Requires a locally supported native affinity mapping for the Bus route at startup.</summary>
    /// <returns>This builder, for chaining.</returns>
    IBusContractBuilder<TMessage> RequireRoutingAffinity();

    /// <summary>
    /// Pins the delivery mode for autonomous Bus publishes of this message, overriding the host
    /// <see cref="MessagingOptions.DefaultDeliveryMode"/>. A per-call <see cref="PublishOptions.DeliveryMode"/> still
    /// overrides it, and an enlisted publish is always durable.
    /// </summary>
    /// <param name="mode">The delivery mode.</param>
    /// <returns>This builder, for chaining.</returns>
    IBusContractBuilder<TMessage> WithDeliveryMode(DeliveryMode mode);
}

/// <summary>Configures the Queue route of one message contract.</summary>
/// <typeparam name="TMessage">The message type the contract describes.</typeparam>
[PublicAPI]
public interface IQueueContractBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Requires a locally supported native affinity mapping for the Queue route at startup.</summary>
    /// <returns>This builder, for chaining.</returns>
    IQueueContractBuilder<TMessage> RequireRoutingAffinity();

    /// <summary>
    /// Pins the delivery mode for autonomous Queue enqueues of this message, overriding the host
    /// <see cref="MessagingOptions.DefaultDeliveryMode"/>. A per-call <see cref="QueueOptions.DeliveryMode"/> still
    /// overrides it, and an enlisted enqueue is always durable.
    /// </summary>
    /// <param name="mode">The delivery mode.</param>
    /// <returns>This builder, for chaining.</returns>
    IQueueContractBuilder<TMessage> WithDeliveryMode(DeliveryMode mode);
}

/// <summary>
/// Collects one <c>Message&lt;T&gt;</c> declaration until its contribution completes, then freezes it into an immutable
/// <see cref="MessageContract"/>.
/// </summary>
internal abstract class MessageContractBuilder
{
    private bool _completed;

    public abstract MessageContract Build();

    public MessageContract Complete()
    {
        _completed = true;
        return Build();
    }

    // The chained calls land after Message<T> returns, so the declaration is only read once its contribution callback
    // ends. A builder kept past that point would change nothing, so the late call fails instead of being lost.
    protected void EnsureNotCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                "A message contract can only be configured inside the ConfigureMessaging callback that declared it."
            );
        }
    }
}

internal sealed class MessageContractBuilder<TMessage>
    : MessageContractBuilder,
        IMessageContractBuilder<TMessage>,
        IBusContractBuilder<TMessage>,
        IQueueContractBuilder<TMessage>
    where TMessage : class
{
    private readonly string _name;
    private readonly string _version;
    private Func<TMessage, string?>? _correlationSelector;
    private MessageContractLaneSettings _bus;
    private MessageContractLaneSettings _queue;

    public MessageContractBuilder(string name, string version)
    {
        MessagingOptions.ValidateMessageName(name);
        _name = name;
        _version = MessagingOptions.ValidateContractVersion(version);
    }

    public IMessageContractBuilder<TMessage> CorrelateBy(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);
        EnsureNotCompleted();
        _correlationSelector = selector;
        return this;
    }

    public IMessageContractBuilder<TMessage> OnBus(Action<IBusContractBuilder<TMessage>> configure)
    {
        Argument.IsNotNull(configure);
        EnsureNotCompleted();
        configure(this);
        return this;
    }

    public IMessageContractBuilder<TMessage> OnQueue(Action<IQueueContractBuilder<TMessage>> configure)
    {
        Argument.IsNotNull(configure);
        EnsureNotCompleted();
        configure(this);
        return this;
    }

    IBusContractBuilder<TMessage> IBusContractBuilder<TMessage>.RequireRoutingAffinity()
    {
        EnsureNotCompleted();
        _bus = _bus with { RequiresRoutingAffinity = true };
        return this;
    }

    IBusContractBuilder<TMessage> IBusContractBuilder<TMessage>.WithDeliveryMode(DeliveryMode mode)
    {
        Argument.IsInEnum(mode);
        EnsureNotCompleted();
        _bus = _bus with { DeliveryMode = mode };
        return this;
    }

    IQueueContractBuilder<TMessage> IQueueContractBuilder<TMessage>.RequireRoutingAffinity()
    {
        EnsureNotCompleted();
        _queue = _queue with { RequiresRoutingAffinity = true };
        return this;
    }

    IQueueContractBuilder<TMessage> IQueueContractBuilder<TMessage>.WithDeliveryMode(DeliveryMode mode)
    {
        Argument.IsInEnum(mode);
        EnsureNotCompleted();
        _queue = _queue with { DeliveryMode = mode };
        return this;
    }

    public override MessageContract Build()
    {
        var selector = _correlationSelector;

        return new MessageContract(
            typeof(TMessage),
            _name,
            _version,
            selector,
            selector is null ? null : message => selector((TMessage)message),
            _bus,
            _queue
        );
    }
}

/// <summary>Settings that apply to one lane's route of a message contract.</summary>
/// <param name="RequiresRoutingAffinity">Whether startup requires a native affinity mapping for the route.</param>
/// <param name="DeliveryMode">The pinned delivery mode, or <see langword="null"/> for the host default.</param>
internal readonly record struct MessageContractLaneSettings(bool RequiresRoutingAffinity, DeliveryMode? DeliveryMode)
{
    public string Describe()
    {
        if (!RequiresRoutingAffinity && DeliveryMode is null)
        {
            return "defaults";
        }

        return string.Join(
            ", ",
            new[]
            {
                RequiresRoutingAffinity ? "routing affinity required" : null,
                DeliveryMode is { } mode ? $"delivery mode {mode}" : null,
            }.Where(static part => part is not null)
        );
    }
}

/// <summary>
/// One frozen <c>Message&lt;T&gt;(name, version)</c> declaration, recorded once per message type in the service
/// collection. It applies to both lanes, and two declarations for one type merge only when they are identical.
/// </summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Name">The logical message name both lanes resolve the type to.</param>
/// <param name="Version">The contract schema version.</param>
/// <param name="DeclaredCorrelationSelector">
/// The selector as declared, kept for comparison: two declarations match only when they pass the same delegate, which is
/// the case when both come from one shared declaration method.
/// </param>
/// <param name="CorrelationSelector">The selector adapted to the untyped publish pipeline.</param>
/// <param name="Bus">Settings of the Bus route.</param>
/// <param name="Queue">Settings of the Queue route.</param>
internal sealed record MessageContract(
    Type MessageType,
    string Name,
    string Version,
    Delegate? DeclaredCorrelationSelector,
    Func<object, string?>? CorrelationSelector,
    MessageContractLaneSettings Bus,
    MessageContractLaneSettings Queue
)
{
    /// <summary>Whether two declarations describe the same contract and may merge.</summary>
    public bool IsSameDeclarationAs(MessageContract other)
    {
        return MessageType == other.MessageType
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Version, other.Version, StringComparison.Ordinal)
            && Equals(DeclaredCorrelationSelector, other.DeclaredCorrelationSelector)
            && Bus == other.Bus
            && Queue == other.Queue;
    }

    /// <summary>The route registration this contract contributes to one lane.</summary>
    public MessageRegistration ToRegistration(MessageLane lane)
    {
        var settings = lane == MessageLane.Bus ? Bus : Queue;

        return new MessageRegistration(
            MessageType,
            lane,
            Name,
            CorrelationSelector,
            ProviderConfigs: new Dictionary<Type, object>(),
            Consumers: [],
            ContractVersion: Version,
            RequiresRoutingAffinity: settings.RequiresRoutingAffinity,
            DeliveryMode: settings.DeliveryMode
        );
    }

    /// <summary>Renders the declaration the way it was written, so a conflict message shows both sides.</summary>
    public string Describe()
    {
        return $"Message<{MessageType.Name}>(\"{Name}\", \"{Version}\") "
            + $"[correlation {(DeclaredCorrelationSelector is null ? "none" : "set")}; "
            + $"Bus: {Bus.Describe()}; Queue: {Queue.Describe()}]";
    }
}
