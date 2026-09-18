// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// The receive-stage context handed to receive middleware: the inbound raw envelope with the
/// consumer's identity known, before contract-version validation and deserialization.
/// </summary>
/// <remarks>
/// <para>
/// There is no typed payload at this stage, so the context is non-generic; a typed registration
/// (<c>AddReceiveMiddlewareFor&lt;TMiddleware, TMessage&gt;</c>) already expresses the payload-type
/// match. Identity (<see cref="MessageId"/>, <see cref="MessageName"/>, <see cref="GroupName"/>,
/// <see cref="Lane"/>) is immutable and enforced: writes to the identity headers through
/// <see cref="SetHeader"/> or <see cref="RemoveHeader"/> always throw, even before completion.
/// </para>
/// <para>
/// Envelope transformation is copy-on-write: the first mutation clones the received headers, the
/// received dictionary itself is never mutated, and <see cref="Headers"/> and <see cref="Body"/>
/// always show the view the next component will see. The received envelope is retained unmodified
/// for poison storage.
/// </para>
/// <para>
/// All state-mutating members throw <see cref="InvalidOperationException"/> once the inner pipeline
/// has completed (the <c>next</c> delegate returned). <see cref="Skip"/> and <see cref="Reject"/>
/// are mutually exclusive; declaring both throws. Cancellation is not a declared outcome: it flows
/// through <see cref="OperationCanceledException"/> as elsewhere in the pipeline.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ReceiveContext
{
    private readonly Dictionary<string, string?> _receivedHeaders;
    private Dictionary<string, string?>? _currentHeaders;
    private bool _isCompleted;

    internal ReceiveContext(
        string messageId,
        string messageName,
        string? groupName,
        MessageLane lane,
        Type messageType,
        string? consumerContractVersion,
        IDictionary<string, string?> headers,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    )
    {
        MessageId = Argument.IsNotNullOrWhiteSpace(messageId);
        MessageName = Argument.IsNotNullOrWhiteSpace(messageName);
        GroupName = groupName;
        Lane = lane;
        MessageType = Argument.IsNotNull(messageType);
        ConsumerContractVersion = consumerContractVersion;
        _receivedHeaders = new Dictionary<string, string?>(headers, StringComparer.Ordinal);
        Headers = _receivedHeaders;
        Body = body;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the unique identifier of the inbound message.</summary>
    public string MessageId { get; }

    /// <summary>Gets the message name that routed this delivery to its consumer.</summary>
    public string MessageName { get; }

    /// <summary>Gets the consumer group receiving this delivery, when one applies.</summary>
    public string? GroupName { get; }

    /// <summary>
    /// Gets the delivery lane that produced this delivery: <see cref="MessageLane.Bus"/> for
    /// broadcast dispatch or <see cref="MessageLane.Queue"/> for point-to-point dispatch. The lane is
    /// registration-derived from the consumer that delivered the envelope, never from a header.
    /// </summary>
    public MessageLane Lane { get; }

    /// <summary>Gets the consumer's declared payload type from the matched registration.</summary>
    public Type MessageType { get; }

    /// <summary>
    /// Gets the contract version declared by the matched consumer registration, or
    /// <see langword="null"/> for runtime subscriptions that carry no durable contract identity.
    /// </summary>
    public string? ConsumerContractVersion { get; }

    /// <summary>
    /// Gets the current header view exactly as the next component will see it, starting from the
    /// received headers and reflecting prior <see cref="SetHeader"/> and <see cref="RemoveHeader"/>
    /// calls.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Headers { get; private set; }

    /// <summary>
    /// Gets the current raw body view exactly as the next component will see it. Reflects the most
    /// recent <see cref="ReplaceBody"/> call.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; private set; }

    /// <summary>Gets the cancellation token currently active for this delivery.</summary>
    public CancellationToken CancellationToken { get; private set; }

    /// <summary>Gets the outcome the middleware declared, if any. Read by the receive pipeline.</summary>
    internal ReceiveOutcome Outcome { get; private set; }

    /// <summary>Gets the reason supplied with <see cref="Skip"/> or <see cref="Reject"/>.</summary>
    internal string? OutcomeReason { get; private set; }

    /// <summary>Gets the cause supplied with <see cref="Reject"/>.</summary>
    internal Exception? RejectCause { get; private set; }

    /// <summary>
    /// Replaces the raw message body the inner pipeline will deserialize.
    /// </summary>
    /// <param name="body">The replacement body bytes.</param>
    /// <exception cref="InvalidOperationException">Thrown when called after the receive pipeline has completed.</exception>
    public void ReplaceBody(ReadOnlyMemory<byte> body)
    {
        _ThrowIfCompleted();
        Body = body;
    }

    /// <summary>
    /// Adds or overwrites a header in the current envelope view, copy-on-write.
    /// </summary>
    /// <param name="key">The header key. Must not be null or whitespace.</param>
    /// <param name="value">The header value. May be <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="key"/> is one of the identity headers (<see cref="Messaging.Headers.MessageId"/>,
    /// <see cref="Messaging.Headers.MessageName"/>, <see cref="Messaging.Headers.Group"/>, <see cref="Messaging.Headers.Exception"/>),
    /// or when called after the receive pipeline has completed.
    /// </exception>
    public void SetHeader(string key, string? value)
    {
        _ThrowIfCompleted();
        Argument.IsNotNullOrWhiteSpace(key);

        if (_IsIdentityHeader(key))
        {
            throw new InvalidOperationException($"Header `{key}` carries the message identity and cannot be modified.");
        }

        WritableHeaders[key] = value;
        Headers = WritableHeaders;
    }

    /// <summary>
    /// Removes a header from the current envelope view, copy-on-write.
    /// </summary>
    /// <param name="key">The header key. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="key"/> is one of the identity headers (<see cref="Messaging.Headers.MessageId"/>,
    /// <see cref="Messaging.Headers.MessageName"/>, <see cref="Messaging.Headers.Group"/>, <see cref="Messaging.Headers.Exception"/>),
    /// or when called after the receive pipeline has completed.
    /// </exception>
    public void RemoveHeader(string key)
    {
        _ThrowIfCompleted();
        Argument.IsNotNullOrWhiteSpace(key);

        if (_IsIdentityHeader(key))
        {
            throw new InvalidOperationException($"Header `{key}` carries the message identity and cannot be modified.");
        }

        WritableHeaders.Remove(key);
        Headers = WritableHeaders;
    }

    /// <summary>
    /// Declares a skip outcome: the delivery is committed and dropped without consumer invocation,
    /// storage, or an exhausted callback. Use for cheap header-based filtering on shared topics.
    /// </summary>
    /// <param name="reason">A human-readable reason recorded in logs and telemetry.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called after the receive pipeline has completed, or when a
    /// <see cref="Reject"/> outcome was already declared.
    /// </exception>
    public void Skip(string reason)
    {
        _ThrowIfCompleted();
        Argument.IsNotNullOrWhiteSpace(reason);

        if (Outcome == ReceiveOutcome.Reject)
        {
            throw new InvalidOperationException("Cannot skip after rejecting the message.");
        }

        Outcome = ReceiveOutcome.Skip;
        OutcomeReason = reason;
    }

    /// <summary>
    /// Declares a reject outcome: the delivery is committed as poison-on-arrival with the received
    /// (untransformed) envelope and the exhausted callback fires with the supplied cause. Use when
    /// the envelope is determined unprocessable — a deterministic payload defect must not burn retry budget.
    /// </summary>
    /// <param name="reason">A human-readable reason recorded in logs and telemetry.</param>
    /// <param name="cause">An optional exception recorded with the poison row.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called after the receive pipeline has completed, or when a
    /// <see cref="Skip"/> outcome was already declared.
    /// </exception>
    public void Reject(string reason, Exception? cause = null)
    {
        _ThrowIfCompleted();
        Argument.IsNotNullOrWhiteSpace(reason);

        if (Outcome == ReceiveOutcome.Skip)
        {
            throw new InvalidOperationException("Cannot reject after skipping the message.");
        }

        Outcome = ReceiveOutcome.Reject;
        OutcomeReason = reason;
        RejectCause = cause;
    }

    /// <summary>
    /// Replaces the active cancellation token forwarded to the remaining middleware and the inner
    /// receive pipeline.
    /// </summary>
    /// <param name="cancellationToken">The replacement cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when called after the receive pipeline has completed.</exception>
    public void SetCancellationToken(CancellationToken cancellationToken)
    {
        _ThrowIfCompleted();
        CancellationToken = cancellationToken;
    }

    internal void MarkCompleted()
    {
        _isCompleted = true;
    }

    /// <summary>
    /// The dictionary the next component should read. The first mutation clones the received
    /// headers; the received dictionary itself is never touched, so it stays intact for poison storage.
    /// </summary>
    private Dictionary<string, string?> WritableHeaders =>
        _currentHeaders ??= new Dictionary<string, string?>(_receivedHeaders, StringComparer.Ordinal);

    private void _ThrowIfCompleted()
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("ReceiveContext is read-only after next() returned.");
        }
    }

    private static bool _IsIdentityHeader(string key)
    {
        return key
            is Messaging.Headers.MessageId
                or Messaging.Headers.MessageName
                or Messaging.Headers.Group
                or Messaging.Headers.Exception;
    }
}

/// <summary>The short-circuit outcome a receive middleware declared for a delivery.</summary>
internal enum ReceiveOutcome
{
    /// <summary>No outcome declared yet; the pipeline treats a return without one as a reject.</summary>
    None,

    /// <summary>Commit the delivery without consumer invocation, storage, or an exhausted callback.</summary>
    Skip,

    /// <summary>Commit the delivery as poison-on-arrival with the received envelope.</summary>
    Reject,
}
