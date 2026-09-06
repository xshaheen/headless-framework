// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;

namespace Headless.Messaging;

internal interface ICompletablePublishContext
{
    void MarkCompleted();
}

/// <summary>Object-typed publish context shared by publish middleware.</summary>
[PublicAPI]
public abstract class PublishContext
{
    private protected PublishContext(
        object? content,
        Type messageType,
        Type concreteMessageType,
        MessageLane lane,
        MessageOptions? options,
        DeliveryDecision decision,
        bool deliveryFrozen,
        CancellationToken cancellationToken
    )
    {
        Content = content;
        MessageType = Argument.IsNotNull(messageType);
        ConcreteMessageType = Argument.IsNotNull(concreteMessageType);
        Lane = lane;
        RequestedDeliveryMode = decision.RequestedMode;
        ResolvedDeliveryMode = decision.ResolvedMode;
        OptionsCore = options;
        DelayTime = decision.Delay;
        ScheduledAt = decision.ScheduledAt;
        PublishAt = decision.PublishAt;
        IsTransactional = decision.IsTransactional;
        DeliveryFrozen = deliveryFrozen;
        Headers = _CreateHeaders(options);
        MessageName = options?.MessageName;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the message payload being published. May be <see langword="null"/>.</summary>
    public object? Content { get; }

    /// <summary>Gets the declared message contract used for middleware and routing.</summary>
    public Type MessageType { get; }

    /// <summary>Gets the concrete payload type while <see cref="MessageType"/> retains the declared contract.</summary>
    public Type ConcreteMessageType { get; internal set; }

    /// <summary>
    /// Gets the authoritative publish lane for this operation.
    /// </summary>
    public MessageLane Lane { get; }

    /// <summary>Gets the delivery mode requested by the caller before resolution.</summary>
    public DeliveryMode RequestedDeliveryMode { get; }

    /// <summary>Gets the delivery mode selected before middleware executes.</summary>
    public DeliveryMode ResolvedDeliveryMode { get; }

    /// <summary>Gets the currently active cancellation token for this publish operation.</summary>
    public CancellationToken CancellationToken { get; private set; }

    /// <summary>Gets the current publish headers snapshot.</summary>
    public MessageHeader Headers { get; private set; }

    /// <summary>Gets the currently selected message name override, if any.</summary>
    public string? MessageName { get; private set; }

    /// <summary>
    /// Gets the current publish options for this operation.
    /// Cast to <see cref="PublishOptions"/> for bus operations or <see cref="QueueOptions"/> for queue operations.
    /// </summary>
    public MessageOptions? Options => OptionsCore;

    /// <summary>Gets the scheduled delay for this operation. <see langword="null"/> means immediate publish.</summary>
    public TimeSpan? DelayTime { get; private set; }

    /// <summary>Gets the caller's absolute schedule for this operation, when one was supplied instead of a delay.</summary>
    public DateTimeOffset? ScheduledAt { get; }

    /// <summary>Gets the resolved UTC not-before timestamp for delayed delivery.</summary>
    public DateTimeOffset? PublishAt { get; }

    /// <summary>Gets whether durable capture is enlisted in the ambient commit boundary.</summary>
    public bool IsTransactional { get; }

    private bool DeliveryFrozen { get; }

    private protected MessageOptions? OptionsCore { get; set; }

    /// <summary>
    /// Replaces the active cancellation token forwarded to downstream middleware and the inner publisher.
    /// Must not be called after the <c>next()</c> delegate has returned.
    /// </summary>
    /// <param name="cancellationToken">The replacement cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when called after the publish pipeline has completed (R10).</exception>
    public void SetCancellationToken(CancellationToken cancellationToken)
    {
        ThrowIfCompleted();
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Replaces the active publish options forwarded to the inner publisher.
    /// Also refreshes the <see cref="Headers"/> and <see cref="MessageName"/> snapshots from the new options.
    /// Must not be called after the <c>next()</c> delegate has returned.
    /// </summary>
    /// <param name="options">The replacement options, or <see langword="null"/> to clear all option overrides.</param>
    /// <exception cref="InvalidOperationException">Thrown when called after the publish pipeline has completed (R10).</exception>
    public void WithOptions(MessageOptions? options)
    {
        ThrowIfCompleted();
        if (DeliveryFrozen && (options?.DeliveryMode ?? RequestedDeliveryMode) != RequestedDeliveryMode)
        {
            throw new InvalidOperationException("Publish middleware cannot change the resolved delivery mode.");
        }

        if (DeliveryFrozen && options?.Delay != DelayTime)
        {
            throw new InvalidOperationException("Publish middleware cannot change the resolved delivery delay.");
        }

        if (DeliveryFrozen && options?.ScheduledAt != ScheduledAt)
        {
            throw new InvalidOperationException("Publish middleware cannot change the resolved delivery schedule.");
        }

        OptionsCore = options;
        RefreshOptionSnapshot(options);
    }

    /// <summary>Replaces the delay on a manually constructed context.</summary>
    public void WithDelayTime(TimeSpan? delayTime)
    {
        ThrowIfCompleted();
        if (DeliveryFrozen && delayTime != DelayTime)
        {
            throw new InvalidOperationException("Publish middleware cannot change the resolved delivery delay.");
        }

        DelayTime = delayTime;
    }

    private protected void RefreshOptionSnapshot(MessageOptions? options)
    {
        Headers = _CreateHeaders(options);
        MessageName = options?.MessageName;
    }

    private static MessageHeader _CreateHeaders(MessageOptions? options)
    {
        return options?.Headers is null ? new MessageHeader() : new MessageHeader(options.Headers);
    }

    private protected bool IsCompleted { get; private set; }

    private protected void Complete()
    {
        IsCompleted = true;
    }

    private protected void ThrowIfCompleted()
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("PublishContext is read-only after next() returned (R10).");
        }
    }
}

/// <summary>Strongly-typed publish context for middleware registered against a specific message type.</summary>
/// <typeparam name="TMessage">The message type being published.</typeparam>
/// <remarks>Initializes a new instance of the <see cref="PublishContext{TMessage}"/> class.</remarks>
[PublicAPI]
public sealed class PublishContext<TMessage> : PublishContext, ICompletablePublishContext
{
    /// <summary>Initializes a publish context for direct construction by middleware tests and tooling.</summary>
    /// <param name="content">The message payload.</param>
    /// <param name="lane">The publish lane.</param>
    /// <param name="options">The message options, including the delivery mode override and delay.</param>
    /// <param name="defaultDeliveryMode">The host delivery mode inherited when the options do not specify one.</param>
    /// <param name="now">The resolution timestamp used to calculate <see cref="PublishContext.PublishAt"/> in UTC.</param>
    /// <param name="isTransactional">Whether to resolve delivery against a compatible ambient commit boundary.</param>
    /// <param name="cancellationToken">The token forwarded to middleware.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The lane or effective delivery mode is undefined, or the delay is nonpositive or overflows the timestamp range.
    /// </exception>
    /// <exception cref="InvalidOperationException">Direct delivery specifies a delay.</exception>
    public PublishContext(
        TMessage? content,
        MessageLane lane,
        MessageOptions? options,
        DeliveryMode defaultDeliveryMode,
        DateTimeOffset now,
        bool isTransactional = false,
        CancellationToken cancellationToken = default
    )
        : base(
            content,
            typeof(TMessage),
            content?.GetType() ?? typeof(TMessage),
            lane,
            options,
            DeliveryDecisionResolver.Resolve(
                lane,
                options?.DeliveryMode ?? defaultDeliveryMode,
                options?.Delay,
                isTransactional ? DeliveryCoordinationStatus.Compatible : DeliveryCoordinationStatus.None,
                now.ToUniversalTime()
            ),
            deliveryFrozen: false,
            cancellationToken
        ) { }

    internal PublishContext(
        TMessage? content,
        MessageLane lane,
        MessageOptions? options,
        DeliveryDecision decision,
        CancellationToken cancellationToken
    )
        : this(
            content,
            content?.GetType() ?? typeof(TMessage),
            lane,
            options,
            decision,
            deliveryFrozen: true,
            cancellationToken
        ) { }

    internal PublishContext(
        TMessage? content,
        Type concreteMessageType,
        MessageLane lane,
        MessageOptions? options,
        DeliveryDecision decision,
        bool deliveryFrozen,
        CancellationToken cancellationToken
    )
        : base(
            content,
            typeof(TMessage),
            concreteMessageType,
            lane,
            options,
            decision,
            deliveryFrozen,
            cancellationToken
        ) { }

    /// <summary>Gets the strongly-typed message payload being published. May be <see langword="null"/>.</summary>
    public new TMessage? Content => (TMessage?)base.Content;

    /// <summary>
    /// Gets or sets the current publish options before the inner publisher runs.
    /// Cast to <see cref="PublishOptions"/> for bus operations or <see cref="QueueOptions"/> for queue operations.
    /// </summary>
    public new MessageOptions? Options
    {
        get => OptionsCore;
        set { WithOptions(value); }
    }

    /// <summary>Gets or sets the delay on a manually constructed context.</summary>
    public new TimeSpan? DelayTime
    {
        get => base.DelayTime;
        set { WithDelayTime(value); }
    }

    /// <summary>
    /// Marks this context as completed, making all mutator properties and methods throw
    /// <see cref="InvalidOperationException"/> on subsequent calls. Called by the runtime after
    /// the publish pipeline's <c>next()</c> delegate returns.
    /// </summary>
    public void MarkCompleted()
    {
        Complete();
    }
}
