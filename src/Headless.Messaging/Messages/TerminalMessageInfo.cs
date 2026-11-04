// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Describes a stored message the framework gave up on, after its terminal state was persisted.
/// Passed to the <see cref="RetryPolicyOptions.OnTerminal"/> callback.
/// </summary>
[PublicAPI]
public sealed class TerminalMessageInfo
{
    /// <summary>
    /// Gets the live per-message dispatch scope's <see cref="IServiceProvider"/>. Use it to resolve
    /// scoped dependencies that share state with the consume/send attempt (e.g., a unit-of-work or
    /// a request-scoped database session).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider is owned by the dispatch scope that ran the message; the scope is disposed
    /// immediately after the <see cref="RetryPolicyOptions.OnTerminal"/> callback completes.
    /// Resolve services synchronously inside the callback and complete all async work via
    /// <see langword="await"/> before returning. Capturing the provider into a <c>Task.Run</c>,
    /// <c>async void</c> lambda, or any work that outlives the callback will hit
    /// <see cref="ObjectDisposedException"/> at runtime.
    /// </para>
    /// <para>
    /// For the poisoned-on-arrival bypass path (a message that fails before any consume or send
    /// attempt), this provider is a freshly-created scope rather than the dispatch scope — there
    /// is no prior attempt to share state with — but the same lifetime rule still applies.
    /// </para>
    /// </remarks>
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>
    /// Gets the message type indicating whether this was a published or subscribed message.
    /// <see cref="MessageType.Publish"/> for messages that failed to be sent to the broker.
    /// <see cref="MessageType.Subscribe"/> for messages that failed to be processed by subscribers.
    /// </summary>
    public required MessageType MessageType { get; init; }

    /// <summary>
    /// Gets the message object that failed processing.
    /// Contains the headers and value data that caused the failure.
    /// </summary>
    public required Message Message { get; init; }

    /// <summary>
    /// Gets the delivery intent of the failed message.
    /// </summary>
    public required MessageLane Lane { get; init; }

    /// <summary>
    /// Gets the exception that ended delivery, or <see langword="null"/> when no exception caused it.
    /// </summary>
    public required Exception? Exception { get; init; }

    /// <summary>Gets why the framework stopped delivering the message.</summary>
    public required MessageTerminalReason Reason { get; init; }

    /// <summary>
    /// Gets the tenant id carried by <see cref="Headers.TenantId"/>, or <see langword="null"/> when absent.
    /// This is the envelope's tenant, not the ambient tenant of the dispatching host.
    /// </summary>
    public required string? TenantId { get; init; }

    /// <summary>
    /// Gets the storage row identifier of the failed message. Use this together with
    /// <c>Message.Id</c> to correlate dead-letter writes back to the original persisted row.
    /// <see cref="Guid.Empty"/> for a message poisoned on arrival, whose row the receive path stores without one.
    /// </summary>
    public required Guid StorageId { get; init; }

    /// <summary>
    /// Gets the final persisted-retry count at the moment <see cref="RetryPolicyOptions.OnTerminal"/>
    /// fires. Mirrors <c>MediumMessage.Retries</c>; counts persisted-retry pickups only — inline
    /// retries do not advance it. Zero for a message poisoned on arrival.
    /// </summary>
    public required int RetryCount { get; init; }
}
