// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>
/// Configuration options for the NATS JetStream messaging transport.
/// </summary>
public sealed class MessagingNatsOptions
{
    /// <summary>
    /// A NATS server URL or comma-separated list of server URLs used to establish the connection.
    /// The value may embed credentials (for example <c>nats://user:pass@host:4222</c>).
    /// Defaults to <c>"nats://127.0.0.1:4222"</c>.
    /// </summary>
    public string Servers { get; set; } = "nats://127.0.0.1:4222";

    /// <summary>
    /// The number of <c>NatsConnection</c> instances in the shared connection pool. Connections
    /// are selected round-robin; each connection multiplexes many subscribers. Defaults to <c>10</c>.
    /// </summary>
    public int ConnectionPoolSize { get; set; } = 10;

    /// <summary>
    /// When <see langword="true"/> (default), consumer clients auto-create JetStream streams with
    /// a wildcard subject (for example <c>orders.&gt;</c>) derived from <see cref="NormalizeStreamName"/>
    /// on first startup. Individual consumers then use a <c>FilterSubject</c> for precise matching.
    /// </summary>
    /// <remarks>
    /// Auto-creation is multi-instance safe: all instances declare the same wildcard stream, so
    /// concurrent startups do not overwrite each other's subject configuration.
    /// Set to <see langword="false"/> to manage streams externally via the NATS CLI or
    /// infrastructure-as-code tooling when fine-grained stream subject control is needed.
    /// </remarks>
    public bool EnableSubscriberClientStreamAndSubjectCreation { get; set; } = true;

    /// <summary>
    /// Customises the underlying NATS connection options. Because <c>NatsOpts</c> is a record,
    /// use the <c>with</c> expression pattern:
    /// <c>opt.ConfigureConnection = o => o with { ConnectTimeout = TimeSpan.FromSeconds(10) };</c>
    /// <see cref="Servers"/> is applied as <c>Url</c> before this callback runs, so the callback
    /// can safely override or extend it.
    /// </summary>
    public Func<NatsOpts, NatsOpts>? ConfigureConnection { get; set; }

    /// <summary>
    /// Customises the JetStream <c>StreamConfig</c> when streams are auto-created. Applied after
    /// the framework sets the stream name and wildcard subject; use this to adjust retention policy,
    /// storage type, or replication factor.
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
    /// The maximum time to wait for a JetStream stream create-or-update during consumer startup
    /// (in <c>FetchMessageNamesAsync</c>). Defaults to <c>30 seconds</c>.
    /// </summary>
    public TimeSpan StreamCreateTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A function that derives the JetStream stream name from a NATS subject. The default
    /// implementation takes the first dot-separated segment (for example <c>"orders"</c> from
    /// <c>"orders.created"</c>). Override this when your stream naming convention differs.
    /// </summary>
    public Func<string, string> NormalizeStreamName { get; set; } = origin => origin.Split('.')[0];

    internal NatsOpts BuildNatsOpts()
    {
        var opts = NatsOpts.Default with { Url = Servers };
        return ConfigureConnection is not null ? ConfigureConnection(opts) : opts;
    }
}

internal sealed class MessagingNatsOptionsValidator : AbstractValidator<MessagingNatsOptions>
{
    public MessagingNatsOptionsValidator()
    {
        RuleFor(x => x.Servers).NotEmpty();
        RuleFor(x => x.ConnectionPoolSize).GreaterThan(0);
        RuleFor(x => x.StreamCreateTimeout).GreaterThan(TimeSpan.Zero);
    }
}
