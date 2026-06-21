// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Transport;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// Configuration options for the Redis Streams queue transport.
/// </summary>
/// <remarks>
/// When <see cref="Configuration"/> is <see langword="null"/> and no endpoints are specified,
/// a post-configure step defaults the connection to <c>localhost</c> on the standard Redis port.
/// <see cref="StreamEntriesCount"/> and <see cref="ConnectionPoolSize"/> default to <c>10</c>
/// when set to <c>0</c>.
/// </remarks>
public class MessagingRedisOptions
{
    /// <summary>
    /// The native StackExchange.Redis connection options. When <see langword="null"/>,
    /// the transport connects to <c>localhost</c> on the default Redis port.
    /// </summary>
    public ConfigurationOptions? Configuration { get; set; }

    // BrokerAddress is emitted to telemetry and dashboards, so expose only broker endpoints here.
    internal string DisplayEndpoint =>
        Configuration?.EndPoints.Count > 0
            ? string.Join(',', Configuration.EndPoints.Select(BrokerAddressDisplay.Format))
            : string.Empty;

    /// <summary>
    /// The maximum number of stream entries read per poll iteration per stream. Higher values
    /// increase throughput at the cost of latency for the first entry in a batch.
    /// Defaults to <c>10</c> when set to <c>0</c>.
    /// </summary>
    public uint StreamEntriesCount { get; set; }

    /// <summary>
    /// The number of <c>IConnectionMultiplexer</c> instances in the shared connection pool.
    /// Increase when many concurrent consumers cause connection contention. Defaults to <c>10</c>
    /// when set to <c>0</c>.
    /// </summary>
    public uint ConnectionPoolSize { get; set; }

    /// <summary>
    /// Optional callback invoked when an error occurs during message consumption. Use this to
    /// log, alert, or forward failed entries to a dead-letter store.
    /// When <see langword="null"/>, consume errors are logged and the entry is skipped.
    /// </summary>
    public Func<ConsumeErrorContext, Task>? OnConsumeError { get; set; }

    /// <summary>Context passed to <see cref="OnConsumeError"/> when a stream entry fails processing.</summary>
    public record ConsumeErrorContext(Exception Exception, StreamEntry? Entry);
}

internal sealed class MessagingRedisOptionsValidator : AbstractValidator<MessagingRedisOptions>;
