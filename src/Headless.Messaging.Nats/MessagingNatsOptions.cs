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
    /// Allows a NATS consumer client to dynamically create streams with wildcard subjects on startup.
    /// Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// When enabled, streams are created with a wildcard subject pattern (e.g., <c>orders.&gt;</c>)
    /// derived from <see cref="NormalizeStreamName"/>. Individual consumers use <c>FilterSubject</c>
    /// for precise topic matching. This is multi-instance safe — all instances create the same stream
    /// with the same wildcard, so concurrent startups do not overwrite each other's subjects.
    /// <para/>
    /// For production deployments requiring fine-grained stream subject control, set this to <c>false</c>
    /// and manage streams externally via NATS CLI or infrastructure tooling.
    /// </remarks>
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

    internal string GetSanitizedServersForDisplay()
    {
        return string.Join(
            ",",
            Servers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(_SanitizeServerForDisplay)
        );
    }

    private static string _SanitizeServerForDisplay(string server)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
        {
            return server;
        }

        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        var sanitized = builder.Uri.GetLeftPart(UriPartial.Authority);
        if (uri.PathAndQuery is { Length: > 1 })
        {
            sanitized += uri.PathAndQuery;
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            sanitized += uri.Fragment;
        }

        return sanitized;
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
