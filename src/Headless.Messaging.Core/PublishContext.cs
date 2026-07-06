// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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
        IntentType intentType,
        MessageOptions? options,
        TimeSpan? delayTime,
        CancellationToken cancellationToken
    )
    {
        Content = content;
        MessageType = Argument.IsNotNull(messageType);
        IntentType = intentType;
        OptionsCore = options;
        DelayTimeCore = delayTime;
        Headers = _CreateHeaders(options);
        MessageName = options?.MessageName;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the message payload being published. May be <see langword="null"/>.</summary>
    public object? Content { get; }

    /// <summary>Gets the runtime message type being published.</summary>
    public Type MessageType { get; }

    /// <summary>
    /// Gets the publish intent for this operation (<see cref="IntentType.Bus"/> or <see cref="IntentType.Queue"/>).
    /// Available to middleware to make intent-aware decisions without inspecting the concrete options type.
    /// </summary>
    public IntentType IntentType { get; }

    /// <summary>Gets the currently active cancellation token for this publish operation.</summary>
    public CancellationToken CancellationToken { get; private set; }

    /// <summary>Gets the current publish headers snapshot.</summary>
    public MessageHeader Headers { get; private set; }

    /// <summary>Gets the currently selected message name override, if any.</summary>
    public string? MessageName { get; private set; }

    /// <summary>
    /// Gets the current publish options for this operation.
    /// Cast to <see cref="PublishOptions"/> for bus operations or <see cref="EnqueueOptions"/> for queue operations.
    /// </summary>
    public MessageOptions? Options => OptionsCore;

    /// <summary>Gets the scheduled delay for this operation. <see langword="null"/> means immediate publish.</summary>
    public TimeSpan? DelayTime => DelayTimeCore;

    private protected MessageOptions? OptionsCore { get; set; }

    private protected TimeSpan? DelayTimeCore { get; set; }

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
        OptionsCore = options;
        RefreshOptionSnapshot(options);
    }

    /// <summary>
    /// Replaces the scheduled delivery delay forwarded to the inner publisher.
    /// Must not be called after the <c>next()</c> delegate has returned.
    /// </summary>
    /// <param name="delayTime">The replacement delay, or <see langword="null"/> for immediate delivery.</param>
    /// <exception cref="InvalidOperationException">Thrown when called after the publish pipeline has completed (R10).</exception>
    public void WithDelayTime(TimeSpan? delayTime)
    {
        ThrowIfCompleted();
        DelayTimeCore = delayTime;
    }

    private protected void RefreshOptionSnapshot(MessageOptions? options)
    {
        Headers = _CreateHeaders(options);
        MessageName = options?.MessageName;
    }

    private static MessageHeader _CreateHeaders(MessageOptions? options) =>
        options?.Headers is null ? new MessageHeader() : new MessageHeader(options.Headers);

    private protected bool IsCompleted { get; private set; }

    private protected void Complete()
    {
        IsCompleted = true;
    }

    private protected void ThrowIfCompleted()
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("PublishingContext is read-only after next() returned (R10).");
        }
    }
}

/// <summary>Strongly-typed publish context for middleware registered against a specific message type.</summary>
/// <typeparam name="TMessage">The message type being published.</typeparam>
/// <remarks>Initializes a new instance of the <see cref="PublishingContext{TMessage}"/> class.</remarks>
[PublicAPI]
public sealed class PublishingContext<TMessage>(
    TMessage? content,
    IntentType intentType,
    MessageOptions? options,
    TimeSpan? delayTime,
    bool isTransactional = false,
    CancellationToken cancellationToken = default
)
    : PublishContext(content, typeof(TMessage), intentType, options, delayTime, cancellationToken),
        ICompletablePublishContext
{
    /// <summary>Gets the strongly-typed message payload being published. May be <see langword="null"/>.</summary>
    public new TMessage? Content => (TMessage?)base.Content;

    /// <summary>
    /// Gets or sets the current publish options before the inner publisher runs.
    /// Cast to <see cref="PublishOptions"/> for bus operations or <see cref="EnqueueOptions"/> for queue operations.
    /// </summary>
    public new MessageOptions? Options
    {
        get => OptionsCore;
        set { WithOptions(value); }
    }

    /// <summary>Gets or sets the scheduled delay before the inner publisher runs.</summary>
    public new TimeSpan? DelayTime
    {
        get => DelayTimeCore;
        set { WithDelayTime(value); }
    }

    /// <summary>
    /// Gets a value indicating whether the publish is buffered inside an ambient outbox transaction.
    /// </summary>
    public bool IsTransactional { get; init; } = isTransactional;

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
