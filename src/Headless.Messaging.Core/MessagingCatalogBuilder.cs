// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Runs one attribute-declared consumer for one delivery. The Messaging source generator emits one per consumer class:
/// it builds the class from the delivery's service scope, switches on the message type of <paramref name="context"/>, and
/// calls the matching <see cref="IConsume{TMessage}.ConsumeAsync"/> with the typed context.
/// </summary>
/// <param name="services">The service provider of the delivery's scope.</param>
/// <param name="context">The typed <see cref="ConsumeContext{TMessage}"/> of the delivery.</param>
/// <param name="cancellationToken">Cancelled when the delivery is abandoned.</param>
/// <returns>A <see cref="ValueTask"/> that completes when the consumer has handled the message.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask MessageConsumerDispatch(
    IServiceProvider services,
    ConsumeContext context,
    CancellationToken cancellationToken
);

/// <summary>
/// Runs <see cref="IOnSubscriptionEstablished.OnSubscriptionEstablishedAsync"/> on one attribute-declared every-instance
/// consumer. The Messaging source generator emits one per consumer class that implements the hook: it builds the class
/// the same way its <see cref="MessageConsumerDispatch"/> does, calls the hook, and releases the instance it created.
/// </summary>
/// <param name="services">The service provider of the hook's scope.</param>
/// <param name="context">Which subscription was established.</param>
/// <param name="cancellationToken">Cancelled when the subscription stops or the hook's time bound expires.</param>
/// <returns>A <see cref="ValueTask"/> that completes when the consumer has resynchronized.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask SubscriptionEstablishedDispatch(
    IServiceProvider services,
    SubscriptionEstablishedContext context,
    CancellationToken cancellationToken
);

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
    /// <param name="dispatch">The generated dispatch of the consumer class.</param>
    /// <param name="onSubscriptionEstablished">
    /// The generated <see cref="IOnSubscriptionEstablished"/> call of an every-instance consumer class that implements
    /// the hook, or <see langword="null"/> when the class has none.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="identity"/> is empty or longer than <see cref="ConsumerMetadata.ConsumerIdentityMaxLength"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public void AddBusConsumer<TConsumer, TMessage>(
        string identity,
        bool everyInstance,
        MessageConsumerDispatch dispatch,
        SubscriptionEstablishedDispatch? onSubscriptionEstablished = null
    )
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        _Add(
            typeof(TConsumer),
            typeof(TMessage),
            MessageLane.Bus,
            identity,
            everyInstance,
            dispatch,
            onSubscriptionEstablished
        );
    }

    /// <summary>Adds one message handled by a <see cref="QueueConsumerAttribute"/> consumer.</summary>
    /// <typeparam name="TConsumer">The consumer class.</typeparam>
    /// <typeparam name="TMessage">One message the consumer implements <see cref="IConsume{TMessage}"/> for.</typeparam>
    /// <param name="identity">The consumer identity from the attribute.</param>
    /// <param name="dispatch">The generated dispatch of the consumer class.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="identity"/> is empty or longer than <see cref="ConsumerMetadata.ConsumerIdentityMaxLength"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public void AddQueueConsumer<TConsumer, TMessage>(string identity, MessageConsumerDispatch dispatch)
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class
    {
        _Add(
            typeof(TConsumer),
            typeof(TMessage),
            MessageLane.Queue,
            identity,
            everyInstance: false,
            dispatch,
            onSubscriptionEstablished: null
        );
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
        MessageConsumerDispatch dispatch,
        SubscriptionEstablishedDispatch? onSubscriptionEstablished
    )
    {
        // The generator already enforces the full owner.name rule at build time; these checks only keep a hand-written
        // or stale module from reaching durable storage with an identity it cannot hold.
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.HasMaxLength(identity, ConsumerMetadata.ConsumerIdentityMaxLength);
        Argument.IsNotNull(dispatch);

        _consumers.Add(
            new MessagingConsumerDeclaration(
                _source,
                consumerType,
                messageType,
                lane,
                identity,
                everyInstance,
                dispatch,
                onSubscriptionEstablished
            )
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
/// <param name="Dispatch">The generated dispatch that runs the consumer class.</param>
/// <param name="OnSubscriptionEstablished">The generated subscription hook of the class, when it has one.</param>
internal sealed record MessagingConsumerDeclaration(
    string Source,
    Type ConsumerType,
    Type MessageType,
    MessageLane Lane,
    string Identity,
    bool EveryInstance,
    MessageConsumerDispatch Dispatch,
    SubscriptionEstablishedDispatch? OnSubscriptionEstablished
);
