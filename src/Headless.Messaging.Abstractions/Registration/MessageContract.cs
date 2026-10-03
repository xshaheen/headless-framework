// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

namespace Headless.Messaging.Registration;

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

internal sealed class MessageContractBuilder<TMessage> : MessageContractBuilder, IMessageContractBuilder<TMessage>
    where TMessage : class
{
    private readonly string _name;
    private readonly string _version;
    private readonly LaneBuilder _bus;
    private readonly LaneBuilder _queue;
    private Func<TMessage, string?>? _correlationSelector;

    public MessageContractBuilder(string name, string version)
    {
        MessageContractRules.ValidateMessageName(name);
        _name = name;
        _version = MessageContractRules.ValidateContractVersion(version);
        _bus = new LaneBuilder(this);
        _queue = new LaneBuilder(this);
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
        configure(_bus);
        return this;
    }

    public IMessageContractBuilder<TMessage> OnQueue(Action<IQueueContractBuilder<TMessage>> configure)
    {
        Argument.IsNotNull(configure);
        EnsureNotCompleted();
        configure(_queue);
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
            _bus.Build(),
            _queue.Build()
        );
    }

    /// <summary>
    /// Collects one lane's route settings. Each lane has its own instance, so a provider extension written against a
    /// lane's builder reaches only that lane's route.
    /// </summary>
    private sealed class LaneBuilder(MessageContractBuilder<TMessage> owner)
        : IBusContractBuilder<TMessage>,
            IQueueContractBuilder<TMessage>,
            IMessageProviderConfigBuilder<TMessage>
    {
        private readonly ProviderConfigBag _providerConfigs = new();
        private bool _requiresRoutingAffinity;
        private DeliveryMode? _deliveryMode;

        IBusContractBuilder<TMessage> IBusContractBuilder<TMessage>.RequireRoutingAffinity()
        {
            _RequireRoutingAffinity();
            return this;
        }

        IBusContractBuilder<TMessage> IBusContractBuilder<TMessage>.WithDeliveryMode(DeliveryMode mode)
        {
            _SetDeliveryMode(mode);
            return this;
        }

        IQueueContractBuilder<TMessage> IQueueContractBuilder<TMessage>.RequireRoutingAffinity()
        {
            _RequireRoutingAffinity();
            return this;
        }

        IQueueContractBuilder<TMessage> IQueueContractBuilder<TMessage>.WithDeliveryMode(DeliveryMode mode)
        {
            _SetDeliveryMode(mode);
            return this;
        }

        void IMessageProviderConfigBuilder<TMessage>.SetMessageProviderConfig(object config)
        {
            owner.EnsureNotCompleted();
            _providerConfigs.Set(config);
        }

        public MessageContractLaneSettings Build() =>
            new(_requiresRoutingAffinity, _deliveryMode, _providerConfigs.Build());

        private void _RequireRoutingAffinity()
        {
            owner.EnsureNotCompleted();
            _requiresRoutingAffinity = true;
        }

        private void _SetDeliveryMode(DeliveryMode mode)
        {
            Argument.IsInEnum(mode);
            owner.EnsureNotCompleted();
            _deliveryMode = mode;
        }
    }
}

/// <summary>Settings that apply to one lane's route of a message contract.</summary>
/// <param name="RequiresRoutingAffinity">Whether startup requires a native affinity mapping for the route.</param>
/// <param name="DeliveryMode">The pinned delivery mode, or <see langword="null"/> for the host default.</param>
/// <param name="ProviderConfigs">Provider settings for the route, such as a partition key, keyed by config type.</param>
internal sealed record MessageContractLaneSettings(
    bool RequiresRoutingAffinity,
    DeliveryMode? DeliveryMode,
    IReadOnlyDictionary<Type, object> ProviderConfigs
)
{
    // Provider configs compare by value: the provider config types define equality over the selectors they hold, so two
    // declarations from one shared contract method match and two different selectors conflict.
    public bool Equals(MessageContractLaneSettings? other)
    {
        return other is not null
            && RequiresRoutingAffinity == other.RequiresRoutingAffinity
            && DeliveryMode == other.DeliveryMode
            && ProviderConfigs.Count == other.ProviderConfigs.Count
            && ProviderConfigs.All(pair =>
                other.ProviderConfigs.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value)
            );
    }

    public override int GetHashCode() => HashCode.Combine(RequiresRoutingAffinity, DeliveryMode, ProviderConfigs.Count);

    public string Describe()
    {
        if (!RequiresRoutingAffinity && DeliveryMode is null && ProviderConfigs.Count == 0)
        {
            return "defaults";
        }

        return string.Join(
            ", ",
            new[]
            {
                RequiresRoutingAffinity ? "routing affinity required" : null,
                DeliveryMode is { } mode ? $"delivery mode {mode}" : null,
                ProviderConfigs.Count == 0
                    ? null
                    : "provider settings "
                        + string.Join(
                            ", ",
                            ProviderConfigs.Keys.Select(static type => type.Name).Order(StringComparer.Ordinal)
                        ),
            }.Where(static part => part is not null)
        );
    }
}

/// <summary>
/// One frozen <c>Message&lt;T&gt;(name, version)</c> declaration, recorded in the service collection as each
/// contribution completes. It applies to both lanes, and two declarations for one type merge only when they are
/// identical.
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
) : MessageDeclaration(MessageType)
{
    /// <summary>Whether two declarations describe the same contract and may merge.</summary>
    public bool IsSameDeclarationAs(MessageContract other)
    {
        return MessageType == other.MessageType
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Version, other.Version, StringComparison.Ordinal)
            && Equals(DeclaredCorrelationSelector, other.DeclaredCorrelationSelector)
            && Bus.Equals(other.Bus)
            && Queue.Equals(other.Queue);
    }

    /// <summary>Renders the declaration the way it was written, so a conflict message shows both sides.</summary>
    public string Describe()
    {
        return $"Message<{MessageType.Name}>(\"{Name}\", \"{Version}\") "
            + $"[correlation {(DeclaredCorrelationSelector is null ? "none" : "set")}; "
            + $"Bus: {Bus.Describe()}; Queue: {Queue.Describe()}]";
    }
}
