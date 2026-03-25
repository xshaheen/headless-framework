// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>
/// Provides programmatic configuration for the messaging NATS project.
/// </summary>
public sealed class MessagingNatsOptions
{
    /// <summary>
    /// Gets or sets the server url/urls used to connect to the NATs server.
    /// </summary>
    /// <remarks>This may contain username/password information.</remarks>
    public string Servers { get; set; } = "nats://127.0.0.1:4222";

    /// <summary>
    /// Connection pool size, default is 10.
    /// </summary>
    public int ConnectionPoolSize { get; set; } = 10;

    /// <summary>
    /// Allows a nats consumer client to dynamically create a stream and configure the expected subjects on the stream. Defaults to true.
    /// </summary>
    public bool EnableSubscriberClientStreamAndSubjectCreation { get; set; } = true;

    /// <summary>
    /// Customize the NATS connection options. Since <see cref="NatsOpts"/> is a record, use the <c>with</c> pattern:
    /// <code>opt.ConfigureConnection = o => o with { ConnectTimeout = TimeSpan.FromSeconds(10) };</code>
    /// The <see cref="Servers"/> property is applied as <c>Url</c> before this callback runs.
    /// </summary>
    public Func<NatsOpts, NatsOpts>? ConfigureConnection { get; set; }

    /// <summary>
    /// Customize the JetStream stream configuration when streams are auto-created.
    /// </summary>
    public Action<StreamConfig>? StreamOptions { get; set; }

    /// <summary>
    /// Customize the JetStream consumer configuration.
    /// </summary>
    public Action<ConsumerConfig>? ConsumerOptions { get; set; }

    /// <summary>
    /// If you need to get additional native delivery args, you can use this function to write into message headers.
    /// </summary>
    public Func<
        NatsJSMsgMetadata?,
        NatsHeaders?,
        IServiceProvider,
        List<KeyValuePair<string, string>>
    >? CustomHeadersBuilder { get; set; }

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
    }
}
