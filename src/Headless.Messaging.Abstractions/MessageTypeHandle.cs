// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// A message type captured where its generic argument was known, so code that later holds only the handle can reach
/// closed generic code for it through <see cref="Accept{TResult}"/> instead of building a generic type by reflection.
/// </summary>
/// <remarks>
/// One instance exists per message type, so handles compare by reference.
/// </remarks>
internal abstract class MessageTypeHandle
{
    private protected MessageTypeHandle() { }

    /// <summary>The captured message type.</summary>
    public abstract Type Type { get; }

    /// <summary>Calls <see cref="IMessageTypeVisitor{TResult}.Visit{TMessage}"/> with the captured message type.</summary>
    public abstract TResult Accept<TResult>(IMessageTypeVisitor<TResult> visitor);

    /// <summary>Gets the handle of <typeparamref name="TMessage"/>.</summary>
    public static MessageTypeHandle For<TMessage>()
        where TMessage : class => MessageTypeHandle<TMessage>.Instance;
}

internal sealed class MessageTypeHandle<TMessage> : MessageTypeHandle
    where TMessage : class
{
    public static readonly MessageTypeHandle<TMessage> Instance = new();

    private MessageTypeHandle() { }

    public override Type Type => typeof(TMessage);

    public override TResult Accept<TResult>(IMessageTypeVisitor<TResult> visitor) => visitor.Visit<TMessage>();
}

/// <summary>Produces a value from a message type recovered from a <see cref="MessageTypeHandle"/>.</summary>
/// <typeparam name="TResult">The value produced.</typeparam>
internal interface IMessageTypeVisitor<out TResult>
{
    TResult Visit<TMessage>()
        where TMessage : class;
}
