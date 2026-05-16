// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;

namespace Headless.Messaging.Messages;

/// <summary>
/// Contains information about a message that has failed processing and exceeded the retry threshold.
/// This class is used when invoking the <see cref="RetryPolicyOptions.OnExhausted"/> callback.
/// </summary>
[PublicAPI]
public sealed class FailedInfo
{
    /// <summary>
    /// Gets the live per-message dispatch scope's <see cref="IServiceProvider"/>. Use it to resolve
    /// scoped dependencies that share state with the consume/send attempt (e.g., a unit-of-work or
    /// a request-scoped database session).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider is owned by the dispatch scope that ran the message; the scope is disposed
    /// immediately after the <see cref="RetryPolicyOptions.OnExhausted"/> callback completes.
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
    /// Gets the exception that triggered the exhausted retry decision.
    /// </summary>
    public required Exception Exception { get; init; }
}
