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
/// </remarks>
public sealed class RedisMessagingOptions
{
    /// <summary>
    /// The native StackExchange.Redis connection options. When <see langword="null"/>,
    /// the transport connects to <c>localhost</c> on the default Redis port.
    /// </summary>
    /// <remarks>
    /// This is a deliberate full-fidelity pass-through of the StackExchange.Redis type
    /// <see cref="ConfigurationOptions"/>: the whole connection-configuration surface is exposed verbatim so no
    /// StackExchange.Redis option is lost behind a lossy Headless wrapper. It intentionally couples this option to
    /// <c>StackExchange.Redis</c>.
    /// </remarks>
    public ConfigurationOptions? Configuration { get; set; }

    // BrokerAddress is emitted to telemetry and dashboards, so expose only broker endpoints here.
    internal string DisplayEndpoint =>
        Configuration?.EndPoints.Count > 0
            ? string.Join(',', Configuration.EndPoints.Select(BrokerAddressDisplay.Format))
            : string.Empty;

    /// <summary>
    /// The maximum number of stream entries one read takes from each stream. A read that returns a full batch is
    /// followed at once by the next read, so a backlog drains without waiting a poll interval per batch.
    /// Must be greater than <c>0</c>. Defaults to <c>100</c>.
    /// </summary>
    /// <remarks>
    /// Every entry a consumer-group read returns stays pending until the core admits it, so keep the time the core
    /// takes to admit one batch well below <see cref="PendingClaimMinIdleTime"/>; otherwise another consumer claims
    /// entries still waiting their turn and the inbox discards the duplicates.
    /// </remarks>
    public int StreamEntriesCount { get; set; } = 100;

    /// <summary>
    /// How long a stream entry must stay pending, delivered to a consumer and not acknowledged, before another consumer
    /// of the group claims it with <c>XAUTOCLAIM</c>. This is how entries held by a crashed consumer, or left pending
    /// by a rejected delivery, are delivered again. Must be greater than <see cref="TimeSpan.Zero"/>. Defaults to
    /// <c>60 seconds</c>.
    /// </summary>
    /// <remarks>
    /// A durable consumer acknowledges an entry once the core admits it into the inbox, before the handler runs, so
    /// the pending window covers admission only and does not grow with handler duration. A runtime subscription,
    /// which has no consumer identity, acknowledges after an inline handler returns: set this above that handler's
    /// longest run, or its entries are delivered twice.
    /// </remarks>
    public TimeSpan PendingClaimMinIdleTime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a consumer of a group must stay idle, with no pending entries, before the transport deletes it from
    /// the group. A restarted process reads under new consumer names when its machine name changes, so this removes
    /// the names it left behind. <see cref="TimeSpan.Zero"/> keeps every consumer. Defaults to <c>1 hour</c>.
    /// </summary>
    /// <remarks>
    /// The check and the delete run as one script on the server, so a consumer that holds a pending entry is never
    /// deleted, and a live consumer that is deleted while idle is created again by its next read.
    /// </remarks>
    public TimeSpan IdleConsumerDeleteAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a stream keeps an entry. Each publish trims, approximately, the entries older than this from the
    /// stream it appends to, so no stream grows without limit by default. <see cref="TimeSpan.Zero"/> keeps entries
    /// without an age limit. Must be <see cref="TimeSpan.Zero"/> or greater than
    /// <see cref="PendingClaimMinIdleTime"/>. Defaults to <c>7 days</c>.
    /// </summary>
    /// <remarks>
    /// Trimming removes an entry whether or not a consumer group has read or acknowledged it, so a group that stays
    /// offline longer than this misses the entries trimmed meanwhile, and an entry that keeps failing admission is
    /// dropped once it is older than this. The age is measured from the entry's id, which the Redis server stamps,
    /// against the publishing process's clock.
    /// </remarks>
    public TimeSpan StreamMaxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// When <see langword="true"/> (default), each publish follows its <c>XADD</c> with a fire-and-forget
    /// <c>PUBLISH</c> on the stream's wake channel, <c>{stream}:wake</c>, and consumers subscribed to it read the stream
    /// at once instead of at their next poll. A message published to an idle stream is then read within a round trip
    /// rather than within the poll interval. When <see langword="false"/>, the process neither publishes nor subscribes
    /// to wake-ups, and its consumers read only when they poll.
    /// </summary>
    /// <remarks>
    /// An idle stream costs no extra commands. Each publish costs one <c>PUBLISH</c>, which a Redis Cluster forwards to
    /// every node, and wakes each poll loop subscribed to the stream (one per consumer group per process, plus each
    /// every-instance consumer) for one read; under steady traffic a loop reads at most once every 50 milliseconds.
    /// Pub/sub delivers at most once, so polling stays the fallback and a lost wake-up only delays a message to the
    /// next poll. Set the same value on every process: a publisher with wake-ups off leaves consumers to their poll.
    /// </remarks>
    public bool WakeConsumersOnPublish { get; set; } = true;

    /// <summary>
    /// The number of <c>IConnectionMultiplexer</c> instances in the shared connection pool.
    /// Increase when many concurrent consumers cause connection contention.
    /// Must be greater than <c>0</c>. Defaults to <c>10</c>.
    /// </summary>
    public int ConnectionPoolSize { get; set; } = 10;

    /// <summary>
    /// Optional callback invoked when an error occurs during message consumption. Use this to
    /// log or alert on failed entries.
    /// When <see langword="null"/>, consume errors are logged and the entry is skipped.
    /// The callback receives a sanitized exception and an entry containing only its identifier;
    /// raw headers and message bodies are never exposed through this diagnostic surface.
    /// </summary>
    public Func<ConsumeErrorContext, Task>? OnConsumeError { get; set; }

    /// <summary>
    /// Context passed to <see cref="OnConsumeError"/> when a stream entry fails processing.
    /// Transport-created contexts contain a sanitized exception and an identifier-only entry.
    /// </summary>
    public record ConsumeErrorContext(Exception Exception, StreamEntry? Entry);
}

internal sealed class RedisMessagingOptionsValidator : AbstractValidator<RedisMessagingOptions>
{
    public RedisMessagingOptionsValidator()
    {
        RuleFor(x => x.StreamEntriesCount).GreaterThan(0);
        RuleFor(x => x.ConnectionPoolSize).GreaterThan(0);
        RuleFor(x => x.PendingClaimMinIdleTime).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.IdleConsumerDeleteAfter).GreaterThanOrEqualTo(TimeSpan.Zero);

        // A shorter age would trim an entry before any consumer could claim it from a crashed one.
        RuleFor(x => x.StreamMaxAge)
            .Must((options, maxAge) => maxAge == TimeSpan.Zero || maxAge > options.PendingClaimMinIdleTime)
            .WithMessage("StreamMaxAge must be zero, for no age limit, or greater than PendingClaimMinIdleTime.");
    }
}
