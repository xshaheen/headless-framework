// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>Object-typed publish context shared by publish middleware.</summary>
[PublicAPI]
public abstract class PublishContext
{
    private protected PublishContext(
        object? content,
        Type messageType,
        PublishOptions? options,
        TimeSpan? delayTime,
        CancellationToken cancellationToken
    )
    {
        Content = content;
        MessageType = Argument.IsNotNull(messageType);
        OptionsCore = options;
        DelayTimeCore = delayTime;
        Headers = new MessageHeader(
            options?.Headers is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(options.Headers, StringComparer.Ordinal)
        );
        Topic = options?.Topic;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the message payload being published. May be <see langword="null"/>.</summary>
    public object? Content { get; }

    /// <summary>Gets the runtime message type being published.</summary>
    public Type MessageType { get; }

    /// <summary>Gets the currently active cancellation token for this publish operation.</summary>
    public CancellationToken CancellationToken { get; private set; }

    /// <summary>Gets the current publish headers snapshot.</summary>
    public MessageHeader Headers { get; private set; }

    /// <summary>Gets the currently selected topic override, if any.</summary>
    public string? Topic { get; private set; }

    /// <summary>Gets the current publish options for this operation.</summary>
    public PublishOptions? Options => OptionsCore;

    /// <summary>Gets the scheduled delay for this operation. <see langword="null"/> means immediate publish.</summary>
    public TimeSpan? DelayTime => DelayTimeCore;

    private protected PublishOptions? OptionsCore { get; set; }

    private protected TimeSpan? DelayTimeCore { get; set; }

    /// <summary>Replaces the active cancellation token for downstream middleware and the inner publisher.</summary>
    public void WithCancellationToken(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    private protected void RefreshOptionSnapshot(PublishOptions? options)
    {
        Headers = new MessageHeader(
            options?.Headers is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(options.Headers, StringComparer.Ordinal)
        );
        Topic = options?.Topic;
    }
}

/// <summary>Strongly-typed publish context for middleware registered against a specific message type.</summary>
/// <typeparam name="TMessage">The message type being published.</typeparam>
[PublicAPI]
public sealed class PublishingContext<TMessage> : PublishContext
{
    private bool _isCompleted;

    /// <summary>Initializes a new instance of the <see cref="PublishingContext{TMessage}"/> class.</summary>
    public PublishingContext(
        TMessage? content,
        PublishOptions? options,
        TimeSpan? delayTime,
        bool isTransactional = false,
        CancellationToken cancellationToken = default
    )
        : base(content, typeof(TMessage), options, delayTime, cancellationToken)
    {
        IsTransactional = isTransactional;
    }

    /// <summary>Gets the strongly-typed message payload being published. May be <see langword="null"/>.</summary>
    public new TMessage? Content => (TMessage?)base.Content;

    /// <summary>
    /// Gets or sets the current publish options before the inner publisher runs.
    /// </summary>
    public new PublishOptions? Options
    {
        get => OptionsCore;
        set
        {
            _ThrowIfCompleted();
            OptionsCore = value;
            RefreshOptionSnapshot(value);
        }
    }

    /// <summary>Gets or sets the scheduled delay before the inner publisher runs.</summary>
    public new TimeSpan? DelayTime
    {
        get => DelayTimeCore;
        set
        {
            _ThrowIfCompleted();
            DelayTimeCore = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the publish is buffered inside an ambient outbox transaction.
    /// </summary>
    public bool IsTransactional { get; init; }

    internal void MarkCompleted()
    {
        _isCompleted = true;
    }

    private void _ThrowIfCompleted()
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("PublishingContext is read-only after next() returned (R10).");
        }
    }
}
