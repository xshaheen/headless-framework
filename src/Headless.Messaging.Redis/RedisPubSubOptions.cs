// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Transport;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// Configuration options for the Redis Pub/Sub bus transport.
/// </summary>
/// <remarks>
/// Redis Pub/Sub provides at-most-once fan-out delivery. Messages published when no subscriber
/// is active are permanently lost. When <see cref="Configuration"/> is <see langword="null"/> and
/// no endpoints are specified, the transport connects to <c>localhost</c> on the default Redis port.
/// </remarks>
public sealed class RedisPubSubOptions
{
    /// <summary>
    /// Gets or sets the native StackExchange.Redis connection options.
    /// </summary>
    /// <remarks>
    /// This is a deliberate full-fidelity pass-through of the StackExchange.Redis type
    /// <see cref="ConfigurationOptions"/>: the whole connection-configuration surface is exposed verbatim so no
    /// StackExchange.Redis option is lost behind a lossy Headless wrapper. It intentionally couples this option to
    /// <c>StackExchange.Redis</c>.
    /// </remarks>
    public ConfigurationOptions? Configuration { get; set; }

    /// <summary>
    /// Gets or sets an optional callback invoked when a subscriber dispatch fails.
    /// Redis Pub/Sub is at-most-once with no built-in retry; use this hook to record,
    /// alert, or forward failed messages to a dead-letter store.
    /// The <see cref="TransportMessage"/> argument may be <see langword="null"/> if the failure
    /// occurred before deserialization. Exceptions thrown by the callback are suppressed
    /// to avoid masking the original failure.
    /// </summary>
    public Func<Exception, TransportMessage?, Task>? OnDispatchFailed { get; set; }

    internal string DisplayEndpoint =>
        Configuration?.EndPoints.Count > 0
            ? string.Join(',', Configuration.EndPoints.Select(BrokerAddressDisplay.Format))
            : string.Empty;
}

internal sealed class RedisPubSubOptionsValidator : AbstractValidator<RedisPubSubOptions>;
