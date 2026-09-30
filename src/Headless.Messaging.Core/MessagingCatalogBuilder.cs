// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Reliability;

namespace Headless.Messaging;

/// <summary>
/// Collects one host's generated consumer declarations while its consumer registry is built. Each generated
/// <see cref="IMessagingModule"/> writes its <see cref="BusConsumerAttribute"/> and <see cref="QueueConsumerAttribute"/>
/// consumers here, one entry per consumed message.
/// </summary>
/// <remarks>
/// Generated code is the only intended caller, which is why the type is hidden from IntelliSense: no fluent call
/// declares a consumer. Every entry remembers the module that added it, so a conflict across modules can name both.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MessagingCatalogBuilder
{
    private const string _FrameworkSource = "Headless.Messaging.Core";

    private readonly List<MessagingConsumerDeclaration> _consumers = [];
    private string _source = _FrameworkSource;

    internal MessagingCatalogBuilder() { }

    internal IReadOnlyList<MessagingConsumerDeclaration> Consumers => _consumers;

    /// <summary>Adds one message handled by a <see cref="BusConsumerAttribute"/> consumer.</summary>
    /// <typeparam name="TConsumer">The consumer class.</typeparam>
    /// <typeparam name="TMessage">One message the consumer implements <see cref="IConsume{TMessage}"/> for.</typeparam>
    /// <param name="identity">The consumer identity from the attribute.</param>
    /// <param name="everyInstance">Whether every process receives every message.</param>
    /// <param name="policy">The failure policy type from the attribute, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="identity"/> is empty or longer than <see cref="ConsumerMetadata.ConsumerIdentityMaxLength"/>, or
    /// <paramref name="policy"/> does not implement <see cref="IFailurePolicy"/>.
    /// </exception>
    public void AddBusConsumer<TConsumer, TMessage>(string identity, bool everyInstance = false, Type? policy = null)
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        _Add(typeof(TConsumer), typeof(TMessage), MessageLane.Bus, identity, everyInstance, policy);
    }

    /// <summary>Adds one message handled by a <see cref="QueueConsumerAttribute"/> consumer.</summary>
    /// <typeparam name="TConsumer">The consumer class.</typeparam>
    /// <typeparam name="TMessage">One message the consumer implements <see cref="IConsume{TMessage}"/> for.</typeparam>
    /// <param name="identity">The consumer identity from the attribute.</param>
    /// <param name="policy">The failure policy type from the attribute, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="identity"/> is empty or longer than <see cref="ConsumerMetadata.ConsumerIdentityMaxLength"/>, or
    /// <paramref name="policy"/> does not implement <see cref="IFailurePolicy"/>.
    /// </exception>
    public void AddQueueConsumer<TConsumer, TMessage>(string identity, Type? policy = null)
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        _Add(typeof(TConsumer), typeof(TMessage), MessageLane.Queue, identity, everyInstance: false, policy);
    }

    /// <summary>Runs one module's generated registration, attributing its entries to the module.</summary>
    internal void AddModule(Type moduleType, Action<MessagingCatalogBuilder> register)
    {
        _source = moduleType.FullName ?? moduleType.Name;
        try
        {
            register(this);
        }
        finally
        {
            _source = _FrameworkSource;
        }
    }

    private void _Add(
        Type consumerType,
        Type messageType,
        MessageLane lane,
        string identity,
        bool everyInstance,
        Type? policy
    )
    {
        // The generator already enforces the full owner.name rule at build time; these checks only keep a hand-written
        // or stale module from reaching durable storage with an identity it cannot hold.
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.HasMaxLength(identity, ConsumerMetadata.ConsumerIdentityMaxLength);
        if (policy is not null && !typeof(IFailurePolicy).IsAssignableFrom(policy))
        {
            throw new ArgumentException(
                $"Failure policy type {policy.FullName} of consumer '{identity}' must implement {typeof(IFailurePolicy).FullName}.",
                nameof(policy)
            );
        }

        _consumers.Add(
            new MessagingConsumerDeclaration(_source, consumerType, messageType, lane, identity, everyInstance, policy)
        );
    }
}

/// <summary>One message handled by one attribute-declared consumer, as a generated module declared it.</summary>
/// <param name="Source">The module that declared it, for conflict messages.</param>
/// <param name="ConsumerType">The consumer class.</param>
/// <param name="MessageType">The consumed message type.</param>
/// <param name="Lane">The lane the consumer's attribute names.</param>
/// <param name="Identity">The consumer identity.</param>
/// <param name="EveryInstance">Whether every process receives every message; always false on the Queue lane.</param>
/// <param name="Policy">The declared failure policy type, if any.</param>
internal sealed record MessagingConsumerDeclaration(
    string Source,
    Type ConsumerType,
    Type MessageType,
    MessageLane Lane,
    string Identity,
    bool EveryInstance,
    Type? Policy
);
