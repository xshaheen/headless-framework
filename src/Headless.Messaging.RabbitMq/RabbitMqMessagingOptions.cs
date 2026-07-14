// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// Configuration options for the RabbitMQ messaging transport.
/// </summary>
/// <remarks>
/// Credentials (<see cref="UserName"/> and <see cref="Password"/>) are <see langword="required"/> and
/// must be set explicitly. The validator rejects the RabbitMQ default credentials
/// (<c>guest</c>/<c>guest</c>) to prevent accidental use in production environments.
/// </remarks>
// ReSharper disable once InconsistentNaming
public sealed class RabbitMqMessagingOptions
{
    /// <summary>
    /// Default virtual host (value: "/").
    /// </summary>
    /// <remarks> PLEASE KEEP THIS MATCHING THE DOC ABOVE.</remarks>
    public const string DefaultVHost = "/";

    /// <summary>
    /// Default exchange name (value: "messaging.default.router").
    /// </summary>
    public const string DefaultExchangeName = "messaging.default.router";

    /// <summary>The AMQP exchange type used when declaring the topic exchange (<c>"topic"</c>).</summary>
    public const string ExchangeType = "topic";

    /// <summary>
    /// The broker hostname to connect to. For cluster connectivity, supply a comma-separated list
    /// of hostnames (for example <c>"192.168.1.111,192.168.1.112"</c>). Defaults to <c>"localhost"</c>.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    /// Password used to authenticate to the broker. Required — must be configured explicitly with no
    /// default value. The validator rejects <c>"guest"</c> for production safety.
    /// </summary>
    public required string Password { get; set; }

    /// <summary>
    /// Username used to authenticate to the broker. Required — must be configured explicitly with no
    /// default value. The validator rejects <c>"guest"</c> for production safety.
    /// </summary>
    public required string UserName { get; set; }

    /// <summary>
    /// The RabbitMQ virtual host to access on this connection. Defaults to <see cref="DefaultVHost"/> (<c>"/"</c>).
    /// </summary>
    public string VirtualHost { get; set; } = DefaultVHost;

    /// <summary>
    /// The topic exchange name declared on startup. Defaults to <see cref="DefaultExchangeName"/>.
    /// When the messaging version is not <c>"v1"</c>, the version suffix is appended automatically
    /// (for example <c>"messaging.default.router.v2"</c>).
    /// </summary>
    public string ExchangeName { get; set; } = DefaultExchangeName;

    /// <summary>
    /// When <see langword="true"/>, enables publisher confirms on the AMQP channel so that
    /// publish operations wait for the broker to acknowledge receipt. Adds latency in exchange
    /// for at-least-once delivery guarantees at the broker level. Defaults to <see langword="false"/>.
    /// </summary>
    public bool PublishConfirms { get; set; }

    /// <summary>
    /// The TCP port to connect on. Use <c>-1</c> (default) to let the client choose the default
    /// AMQP port (5672) or the TLS port when TLS is configured.
    /// </summary>
    public int Port { get; set; } = -1;

    /// <summary>
    /// Optional AMQP x-arguments applied when queues are declared. These are passed as the
    /// <c>x-arguments</c> map in the AMQP 0-9-1 <c>queue.declare</c> method.
    /// </summary>
    public QueueArgumentsOptions QueueArguments { get; set; } = new();

    /// <summary>
    /// Durability, exclusivity, and auto-delete flags applied when queues are declared.
    /// </summary>
    public QueueRabbitOptions QueueOptions { get; set; } = new();

    /// <summary>
    /// Optional callback that adds extra headers to an inbound message from native RabbitMQ
    /// delivery arguments. Use this to surface broker-native properties (for example delivery
    /// tags, redelivery flags, or routing keys) as framework message headers.
    /// </summary>
    public Func<
        BasicDeliverEventArgs,
        IServiceProvider,
        List<KeyValuePair<string, string>>
    >? CustomHeadersBuilder { get; set; }

    /// <summary>
    /// Optional callback that customises the underlying RabbitMQ <c>ConnectionFactory</c> before
    /// the connection is opened. Use this to configure TLS, heartbeats, or any property not
    /// directly exposed by <see cref="RabbitMqMessagingOptions"/>.
    /// </summary>
    public Action<ConnectionFactory>? ConnectionFactoryOptions { get; set; }

    /// <summary>
    /// Per-channel Quality-of-Service (prefetch) settings. When <see langword="null"/>, no
    /// <c>basic.qos</c> is sent and the broker uses its own defaults.
    /// See <see href="https://www.rabbitmq.com/consumer-prefetch.html"/>.
    /// </summary>
    public BasicQos? BasicQosOptions { get; set; }

    /// <summary>AMQP x-arguments applied when declaring queues.</summary>
    public sealed class QueueArgumentsOptions
    {
        /// <summary>
        /// Sets the <c>x-queue-mode</c> declaration argument. Use <c>"lazy"</c> to keep
        /// messages on disk and reduce memory pressure. Defaults to <see langword="null"/>
        /// (the broker default is used).
        /// </summary>
        public string? QueueMode { get; set; }

        /// <summary>
        /// Sets the <c>x-message-ttl</c> declaration argument — the time in milliseconds
        /// before a message is discarded. Defaults to 864,000,000 ms (10 days).
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public int MessageTTL { get; set; } = 864000000;

        /// <summary>
        /// Sets the <c>x-queue-type</c> declaration argument. Use <c>"quorum"</c> for
        /// replicated, durable queues or <c>"stream"</c> for append-only log semantics.
        /// Defaults to <see langword="null"/> (the broker default <c>"classic"</c> is used).
        /// </summary>
        public string? QueueType { get; set; }
    }

    /// <summary>
    /// Encapsulates the arguments for a <c>basic.qos</c> request applied to each consumer channel.
    /// </summary>
    /// <remarks>
    /// Initialises a new <see cref="BasicQos"/> value.
    /// </remarks>
    /// <param name="prefetchCount">
    /// The maximum number of unacknowledged messages the broker may push to the consumer
    /// before an acknowledgement is received. A value of <c>0</c> means unlimited.
    /// </param>
    /// <param name="global">
    /// When <see langword="false"/> (default), the limit is applied per consumer on the channel.
    /// When <see langword="true"/>, the limit is shared across all consumers on the channel.
    /// </param>
    public sealed class BasicQos(ushort prefetchCount, bool global = false)
    {
        /// <summary>
        /// The maximum number of unacknowledged messages the broker delivers before waiting for
        /// an acknowledgement. <c>0</c> means unlimited. Defaults to <c>0</c>.
        /// </summary>
        public ushort PrefetchCount { get; } = prefetchCount;

        /// <summary>
        /// When <see langword="false"/> (default), the prefetch limit is applied independently
        /// to each new consumer on the channel. When <see langword="true"/>, the limit is shared
        /// across all consumers on the same channel.
        /// </summary>
        public bool Global { get; } = global;
    }

    /// <summary>Queue declaration flags applied when RabbitMQ queues are created.</summary>
    public sealed class QueueRabbitOptions
    {
        /// <summary>
        /// When <see langword="true"/> (default), the queue survives broker restarts.
        /// Set to <see langword="false"/> only for transient, non-critical queues.
        /// </summary>
        public bool Durable { get; set; } = true;

        /// <summary>
        /// When <see langword="true"/>, the queue is restricted to the declaring connection
        /// and is deleted when that connection closes. Defaults to <see langword="false"/>.
        /// </summary>
        public bool Exclusive { get; set; }

        /// <summary>
        /// When <see langword="true"/>, the queue is automatically deleted when the last consumer
        /// unsubscribes. Defaults to <see langword="false"/>.
        /// </summary>
        public bool AutoDelete { get; set; }
    }
}

internal sealed class RabbitMqMessagingOptionsValidator : AbstractValidator<RabbitMqMessagingOptions>
{
    public RabbitMqMessagingOptionsValidator()
    {
        RuleFor(x => x.HostName).NotEmpty().WithMessage("HostName is required");

        RuleFor(x => x.UserName).NotEmpty().WithMessage("UserName is required and must be configured explicitly");
        RuleFor(x => x.UserName)
            .Must(u => !u.Equals("guest", StringComparison.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.UserName))
            .WithMessage("UserName cannot be 'guest' - use a secure username for production environments");

        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required and must be configured explicitly");
        RuleFor(x => x.Password)
            .Must(p => !p.Equals("guest", StringComparison.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Password))
            .WithMessage("Password cannot be 'guest' - use a secure password for production environments");

        RuleFor(x => x.Port)
            .Must(p => p is -1 or (>= 1 and <= 65535))
            .WithMessage("Port must be -1 (default) or between 1 and 65535");

        RuleFor(x => x.VirtualHost).NotEmpty().WithMessage("VirtualHost is required");
        RuleFor(x => x.ExchangeName).NotEmpty().WithMessage("ExchangeName is required");

        RuleFor(x => x.ExchangeName)
            .Must(name =>
            {
                try
                {
                    RabbitMqValidation.ValidateExchangeName(name);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            })
            .When(x => !string.IsNullOrWhiteSpace(x.ExchangeName))
            .WithMessage("Invalid ExchangeName format");
    }
}
