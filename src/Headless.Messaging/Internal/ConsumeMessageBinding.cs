// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// The closed generic code the consume pipeline runs for one message type: it builds the delivery's
/// <see cref="ConsumeContext{TMessage}"/> and calls typed <see cref="IConsumeMiddleware{TContext}"/>. Each consumer
/// descriptor carries the binding of its message type, captured where that type was a generic argument, so a delivery
/// needs neither reflection nor a runtime-compiled delegate.
/// </summary>
internal abstract class ConsumeMessageBinding
{
    private protected ConsumeMessageBinding() { }

    /// <summary>The message type deliveries are deserialized into.</summary>
    public abstract Type MessageType { get; }

    /// <summary>The service type of middleware registered for this message type's typed context.</summary>
    public abstract Type MiddlewareServiceType { get; }

    /// <summary>Gets the binding of <typeparamref name="TMessage"/>.</summary>
    public static ConsumeMessageBinding For<TMessage>()
        where TMessage : class => ConsumeMessageBinding<TMessage>.Instance;

    /// <summary>Gets the binding of the message type <paramref name="handle"/> captured.</summary>
    public static ConsumeMessageBinding From(MessageTypeHandle handle) => handle.Accept(Visitor.Instance);

    /// <summary>
    /// Builds the typed context of one delivery, binding the envelope headers the way every consumer sees them.
    /// </summary>
    public abstract ConsumeContext CreateContext(
        object message,
        MediumMessage mediumMessage,
        MessageHeader headers,
        string? tenantId,
        MessageLane lane
    );

    /// <summary>Calls typed middleware, which <see cref="MiddlewareServiceType"/> resolved, with a context of this type.</summary>
    public abstract ValueTask InvokeMiddlewareAsync(object middleware, ConsumeContext context, Func<ValueTask> next);

    private protected static string? ResolveCorrelationId(IDictionary<string, string?> headers)
    {
        return headers.TryGetValue(Headers.CorrelationId, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private protected static string? ResolveOptionalHeader(IDictionary<string, string?> headers, string name)
    {
        return headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private protected static string ResolveContractVersion(IDictionary<string, string?> headers)
    {
        return ResolveOptionalHeader(headers, Headers.ContractVersion) ?? MessageOptions.InitialContractVersion;
    }

    private protected static DateTimeOffset ResolveTimestamp(IDictionary<string, string?> headers, DateTimeOffset added)
    {
        if (
            headers.TryGetValue(Headers.SentTime, out var sentTime)
            && !string.IsNullOrWhiteSpace(sentTime)
            && DateTimeOffset.TryParse(
                sentTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
        )
        {
            return parsed;
        }

        // MediumMessage.Added is already an instant -- there is no kind left to guess at.
        return added;
    }

    private sealed class Visitor : IMessageTypeVisitor<ConsumeMessageBinding>
    {
        public static readonly Visitor Instance = new();

        public ConsumeMessageBinding Visit<TMessage>()
            where TMessage : class => ConsumeMessageBinding<TMessage>.Instance;
    }
}

internal sealed class ConsumeMessageBinding<TMessage> : ConsumeMessageBinding
    where TMessage : class
{
    public static readonly ConsumeMessageBinding<TMessage> Instance = new();

    private ConsumeMessageBinding() { }

    public override Type MessageType => typeof(TMessage);

    public override Type MiddlewareServiceType => typeof(IConsumeMiddleware<ConsumeContext<TMessage>>);

    public override ConsumeContext CreateContext(
        object message,
        MediumMessage mediumMessage,
        MessageHeader headers,
        string? tenantId,
        MessageLane lane
    )
    {
        var originHeaders = mediumMessage.Origin.Headers;

        // The message id and name are read through the indexer: an envelope missing either is malformed and fails
        // here, before any middleware or handler runs.
        return new ConsumeContext<TMessage>
        {
            Message = (TMessage)message,
            MessageId = originHeaders[Headers.MessageId] ?? string.Empty,
            CorrelationId = ResolveCorrelationId(originHeaders),
            CausationId = ResolveOptionalHeader(originHeaders, Headers.CausationId),
            ContractVersion = ResolveContractVersion(originHeaders),
            TransportAddress = ResolveOptionalHeader(originHeaders, Headers.TransportAddress),
            TenantId = tenantId,
            Lane = lane,
            Headers = headers,
            Timestamp = ResolveTimestamp(originHeaders, mediumMessage.Added),
            MessageName = originHeaders[Headers.MessageName] ?? string.Empty,
        };
    }

    public override ValueTask InvokeMiddlewareAsync(object middleware, ConsumeContext context, Func<ValueTask> next)
    {
        return ((IConsumeMiddleware<ConsumeContext<TMessage>>)middleware).InvokeAsync(
            (ConsumeContext<TMessage>)context,
            next
        );
    }
}
