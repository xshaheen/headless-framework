// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>
/// Configuration options for the NATS JetStream messaging transport.
/// </summary>
public sealed class NatsMessagingOptions
{
    /// <summary>
    /// A NATS server URL or comma-separated list of server URLs used to establish the connection.
    /// The value may embed credentials (for example <c>nats://user:pass@host:4222</c>).
    /// Defaults to <c>"nats://127.0.0.1:4222"</c>.
    /// </summary>
    public string Servers { get; set; } = "nats://127.0.0.1:4222";

    /// <summary>
    /// The number of <c>NatsConnection</c> instances in the shared publish connection pool. Connections
    /// are selected round-robin; each connection is a single TCP socket that already multiplexes many
    /// publishers and subscribers, so <c>1</c> is sufficient for the vast majority of workloads and is the
    /// default. Raise this only as a throughput knob when a single connection's command writer becomes the
    /// publish bottleneck under very high send rates; opening extra sockets otherwise just wastes resources.
    /// </summary>
    public int ConnectionPoolSize { get; set; } = 1;

    /// <summary>
    /// The number of consecutive consume-loop failures (JetStream consumer create/update or message fetch)
    /// tolerated on a single subject listener before it is terminated for a supervised restart with a fresh
    /// connection. The counter resets to zero on any forward progress (a successful consumer bind or fetch).
    /// This bounds in-place spinning when a connection stays unusable but the surfaced error is not one of the
    /// classified connection-failure types; the NATS client keeps reconnecting on its own without a limit, so
    /// this counter is what hands a stuck listener back for a rebuild. Defaults to <c>10</c>.
    /// </summary>
    public int MaxConsecutiveConsumeFailures { get; set; } = 10;

    /// <summary>
    /// How consumer clients, at startup, and publishers, before the first publish of each message, provision the
    /// JetStream streams their subjects live on. The stream name comes from <see cref="NormalizeStreamName"/>, and the
    /// stream carries the key's wildcard, <c>headless.{lane}.{key}.&gt;</c> (plus the bare <c>headless.{lane}.{key}</c>
    /// when a message is named exactly the key), so any host that creates it covers every message on that key. Individual consumers then use a <c>FilterSubject</c> for
    /// precise matching. Defaults to <see cref="NatsStreamProvisioning.Verify"/>, which creates a missing stream but
    /// never rewrites one that already exists.
    /// </summary>
    /// <remarks>
    /// Subject handling is asymmetric in every mode. Subjects the live stream already carries — contributed by
    /// another host or an earlier deployment — are left alone rather than replaced. Subjects this
    /// host requires that the stream does not cover are written under
    /// <see cref="NatsStreamProvisioning.Reconcile"/> and reported as divergence under
    /// <see cref="NatsStreamProvisioning.Verify"/>, because JetStream delivers nothing, and reports no error,
    /// to a filter that matches no subject on the stream.
    /// </remarks>
    public NatsStreamProvisioning StreamProvisioning { get; set; } = NatsStreamProvisioning.Verify;

    /// <summary>
    /// Customises the underlying NATS connection options. Because <c>NatsOpts</c> is a record,
    /// use the <c>with</c> expression pattern:
    /// <c>opt.ConfigureConnection = o => o with { ConnectTimeout = TimeSpan.FromSeconds(10) };</c>
    /// <see cref="Servers"/> is applied as <c>Url</c> before this callback runs, so the callback
    /// can safely override or extend it.
    /// </summary>
    public Func<NatsOpts, NatsOpts>? ConfigureConnection { get; set; }

    /// <summary>
    /// Customises the JetStream <c>StreamConfig</c> when a missing stream is created and when building the
    /// desired configuration for an existing stream under <c>Verify</c> or <c>Reconcile</c>. Use this to
    /// adjust storage, replication, limits, and other supported stream settings. Stream name, subjects, and
    /// retention are provider-owned lane identity.
    /// </summary>
    public Action<StreamConfig>? StreamOptions { get; set; }

    /// <summary>
    /// Customises the JetStream <c>ConsumerConfig</c> for each consumer. Applied after the
    /// framework sets the durable name, filter subject, and deliver policy.
    /// </summary>
    public Action<ConsumerConfig>? ConsumerOptions { get; set; }

    /// <summary>
    /// Optional callback that adds extra headers to an inbound message from native NATS metadata.
    /// Use this to surface JetStream sequence numbers, timestamps, or custom headers as
    /// framework message headers.
    /// </summary>
    public Func<
        NatsJSMsgMetadata?,
        NatsHeaders?,
        IServiceProvider,
        List<KeyValuePair<string, string>>
    >? CustomHeadersBuilder { get; set; }

    /// <summary>
    /// The maximum time to wait for a JetStream stream create-or-update during consumer startup or before the first
    /// publish of a message. Defaults to <c>30 seconds</c>.
    /// </summary>
    public TimeSpan StreamCreateTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A function that derives the JetStream stream key from a message name. The default
    /// implementation takes the first dot-separated segment (for example <c>"orders"</c> from
    /// <c>"orders.created"</c>). Override this when your stream naming convention differs. A name the key does not
    /// prefix gets its own exact subject on the stream instead of the key's wildcard.
    /// </summary>
    /// <remarks>
    /// A normalizer that maps several names the key does not prefix onto one stream (for example
    /// <c>_ =&gt; "app"</c>) needs <see cref="StreamProvisioning"/> set to
    /// <see cref="NatsStreamProvisioning.Reconcile"/>. Each of those names adds its own subject to the shared stream,
    /// and under <see cref="NatsStreamProvisioning.Verify"/> the first host or first publish fixes the stream's
    /// subjects, so every later name fails as divergent. The same holds for a name equal to its key that is published
    /// first without a shard and later with one.
    /// </remarks>
    public Func<string, string> NormalizeStreamName { get; set; } = origin => origin.Split('.')[0];

    /// <summary>
    /// Gets the factory that supplies the application's own NATS connection, or <see langword="null"/> when Headless
    /// opens its own. Set it through <see cref="UseConnection"/>.
    /// </summary>
    internal Func<IServiceProvider, INatsConnection>? ConnectionFactory { get; private set; }

    /// <summary>
    /// Publishes, answers requests, and provisions streams over a NATS connection the application owns, so an
    /// application that also uses NATS directly (key-value or object store, its own subjects) keeps one connection.
    /// </summary>
    /// <param name="factory">
    /// Returns the application's connection, for example
    /// <c>sp =&gt; sp.GetRequiredService&lt;INatsConnection&gt;()</c> after <c>services.AddNatsClient(...)</c>. It runs once,
    /// when the connection pool is first resolved.
    /// </param>
    /// <returns>The same options instance for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Headless never disposes the supplied connection; the application owns its lifetime. <see cref="Servers"/>,
    /// <see cref="ConfigureConnection"/>, and <see cref="ConnectionPoolSize"/> do not apply to it, and a pool size other
    /// than <c>1</c> fails validation.
    /// </para>
    /// <para>
    /// Each consumer client still opens a connection of its own, built from the supplied connection's options. A stuck
    /// consumer is recovered by discarding its connection and opening a new one, which Headless cannot do to a connection
    /// it does not own, and NATS disconnects a slow consumer's whole connection, which would also cut the application's
    /// own traffic on a shared socket.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public NatsMessagingOptions UseConnection(Func<IServiceProvider, INatsConnection> factory)
    {
        ConnectionFactory = Argument.IsNotNull(factory);
        return this;
    }

    internal NatsOpts BuildNatsOpts()
    {
        var opts = NatsOpts.Default with { Url = Servers };
        return ConfigureConnection is not null ? ConfigureConnection(opts) : opts;
    }
}

internal sealed class NatsMessagingOptionsValidator : AbstractValidator<NatsMessagingOptions>
{
    public NatsMessagingOptionsValidator()
    {
        RuleFor(x => x.Servers).NotEmpty();
        RuleFor(x => x.ConnectionPoolSize).GreaterThan(0);
        RuleFor(x => x.ConnectionPoolSize)
            .Equal(1)
            .When(x => x.ConnectionFactory is not null)
            .WithMessage("ConnectionPoolSize must be 1 when UseConnection supplies the application's connection.");
        RuleFor(x => x.MaxConsecutiveConsumeFailures).GreaterThan(0);
        RuleFor(x => x.StreamCreateTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.StreamProvisioning).IsInEnum();
    }
}
