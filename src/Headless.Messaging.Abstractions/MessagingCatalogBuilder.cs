// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Reliability;

namespace Headless.Messaging;

/// <summary>
/// Collects one host's generated consumer declarations while its consumer registry is built. Each generated
/// <see cref="IMessagingModule"/> writes its <see cref="BusConsumerAttribute"/> and <see cref="QueueConsumerAttribute"/>
/// consumers here, one entry per consumed message or answered request.
/// </summary>
/// <remarks>
/// Generated code is the only intended caller, which is why the type is hidden from IntelliSense: no fluent call
/// declares a consumer. Every entry remembers the module that added it, so a conflict across modules can name both.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MessagingCatalogBuilder
{
    /// <summary>
    /// The longest consumer identity a module may declare. Durable inbox storage keys on the identity, so a longer one
    /// cannot be stored.
    /// </summary>
    public const int ConsumerIdentityMaxLength = 200;

    private const string _FrameworkSource = "Headless.Messaging";

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
    /// <param name="failurePolicy">
    /// Creates the failure policy the consumer's attribute declares, or <see langword="null"/> when it declares none.
    /// The host builds it once per identity when its consumer registry is built.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="identity"/> is empty or longer than <see cref="ConsumerIdentityMaxLength"/>, or
    /// an every-instance consumer declares a <paramref name="failurePolicy"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public void AddBusConsumer<TConsumer, TMessage>(
        string identity,
        bool everyInstance,
        MessageConsumerDispatch dispatch,
        SubscriptionEstablishedDispatch? onSubscriptionEstablished = null,
        Func<FailurePolicy>? failurePolicy = null
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
            onSubscriptionEstablished,
            failurePolicy
        );
    }

    /// <summary>Adds one message handled by a <see cref="QueueConsumerAttribute"/> consumer.</summary>
    /// <typeparam name="TConsumer">The consumer class.</typeparam>
    /// <typeparam name="TMessage">One message the consumer implements <see cref="IConsume{TMessage}"/> for.</typeparam>
    /// <param name="identity">The consumer identity from the attribute.</param>
    /// <param name="dispatch">The generated dispatch of the consumer class.</param>
    /// <param name="failurePolicy">
    /// Creates the failure policy the consumer's attribute declares, or <see langword="null"/> when it declares none.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="identity"/> is empty or longer than <see cref="ConsumerIdentityMaxLength"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public void AddQueueConsumer<TConsumer, TMessage>(
        string identity,
        MessageConsumerDispatch dispatch,
        Func<FailurePolicy>? failurePolicy = null
    )
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
            onSubscriptionEstablished: null,
            failurePolicy
        );
    }

    /// <summary>Adds one request answered by a <see cref="QueueConsumerAttribute"/> responder.</summary>
    /// <remarks>
    /// A responder is its request's one Queue consumer. The response type travels with the entry so a host that declares
    /// a responder can check at startup that its transport has a reply channel.
    /// </remarks>
    /// <typeparam name="TConsumer">The responder class.</typeparam>
    /// <typeparam name="TRequest">One request the class implements <see cref="IRespond{TRequest, TResponse}"/> for.</typeparam>
    /// <typeparam name="TResponse">The response type the class answers <typeparamref name="TRequest"/> with.</typeparam>
    /// <param name="identity">The consumer identity from the attribute.</param>
    /// <param name="dispatch">
    /// The generated dispatch of the responder class, which records the value <c>RespondAsync</c> returned on the
    /// delivery's context.
    /// </param>
    /// <param name="failurePolicy">
    /// Creates the failure policy the responder's attribute declares, or <see langword="null"/> when it declares none.
    /// A request still retries only inline and within its deadline.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="identity"/> is empty or longer than <see cref="ConsumerIdentityMaxLength"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public void AddQueueResponder<TConsumer, TRequest, TResponse>(
        string identity,
        MessageConsumerDispatch dispatch,
        Func<FailurePolicy>? failurePolicy = null
    )
        where TConsumer : class, IRespond<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        _Add(
            typeof(TConsumer),
            typeof(TRequest),
            MessageLane.Queue,
            identity,
            everyInstance: false,
            dispatch,
            onSubscriptionEstablished: null,
            failurePolicy,
            typeof(TResponse)
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
        SubscriptionEstablishedDispatch? onSubscriptionEstablished,
        Func<FailurePolicy>? failurePolicy,
        Type? responseType = null
    )
    {
        // The generator already enforces the full owner.name rule at build time; these checks only keep a hand-written
        // or stale module from reaching durable storage with an identity it cannot hold.
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.HasMaxLength(identity, ConsumerIdentityMaxLength);
        Argument.IsNotNull(dispatch);

        // The generator rejects this at build time; a hand-written module would otherwise declare a policy that never
        // runs, because an every-instance delivery is at most once and never stored for a retry.
        if (everyInstance && failurePolicy is not null)
        {
            throw new ArgumentException(
                $"Module {_source} declares every-instance consumer '{identity}' with a failure policy. An "
                    + "every-instance subscription belongs to one process and delivers at most once, with no retry; "
                    + "remove the failure policy or make the consumer competing.",
                nameof(failurePolicy)
            );
        }

        _consumers.Add(
            new MessagingConsumerDeclaration(
                _source,
                consumerType,
                messageType,
                lane,
                identity,
                everyInstance,
                dispatch,
                onSubscriptionEstablished,
                failurePolicy,
                responseType
            )
        );
    }
}

/// <summary>
/// Runs one attribute-declared consumer for one delivery. The Messaging source generator emits one per consumer class:
/// it builds the class from the delivery's service scope, switches on the message type of <paramref name="context"/>, and
/// calls the matching <see cref="IConsume{TMessage}.ConsumeAsync"/> with the typed context. For a request the class
/// answers, it calls <see cref="IRespond{TRequest, TResponse}.RespondAsync"/> instead and records the returned value on
/// <paramref name="context"/> as the reply.
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

/// <summary>One message handled by one attribute-declared consumer, as a generated module declared it.</summary>
/// <param name="Source">The module that declared it, for conflict messages.</param>
/// <param name="ConsumerType">The consumer class.</param>
/// <param name="MessageType">The consumed message type.</param>
/// <param name="Lane">The lane the consumer's attribute names.</param>
/// <param name="Identity">The consumer identity.</param>
/// <param name="EveryInstance">Whether every process receives every message; always false on the Queue lane.</param>
/// <param name="Dispatch">The generated dispatch that runs the consumer class.</param>
/// <param name="OnSubscriptionEstablished">The generated subscription hook of the class, when it has one.</param>
/// <param name="FailurePolicyFactory">Creates the failure policy the attribute declares, when it declares one.</param>
/// <param name="ResponseType">
/// The response type of a responder, or <see langword="null"/> for a consumer that does not answer requests.
/// </param>
internal sealed record MessagingConsumerDeclaration(
    string Source,
    Type ConsumerType,
    Type MessageType,
    MessageLane Lane,
    string Identity,
    bool EveryInstance,
    MessageConsumerDispatch Dispatch,
    SubscriptionEstablishedDispatch? OnSubscriptionEstablished,
    Func<FailurePolicy>? FailurePolicyFactory = null,
    Type? ResponseType = null
);
