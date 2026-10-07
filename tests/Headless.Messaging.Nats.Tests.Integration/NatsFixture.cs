// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Testcontainers.Nats;

namespace Tests;

[UsedImplicitly]
public sealed class NatsFixture : HeadlessNatsFixture
{
    private const int _ConnectionAttempts = 10;

    private static readonly byte[] _NatsConfig = Encoding.UTF8.GetBytes(
        """
        port: 4222
        monitor_port: 8222
        jetstream {}
        """
    );

    private NatsConnection? _connection;

    /// <summary>Gets the NATS connection string.</summary>
    public string ConnectionString => Container.GetConnectionString();

    protected override NatsBuilder Configure()
    {
        return base.Configure().WithResourceMapping(_NatsConfig, "/etc/nats/nats-server.conf");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        // Eagerly establish the shared connection to fail fast on startup issues
        await GetConnectionAsync();
    }

    /// <summary>
    /// Ensures a JetStream stream exists with a wildcard subject so publish tests succeed.
    /// </summary>
    public async Task EnsureStreamAsync(
        string streamName,
        string subjectWildcard,
        StreamConfigRetention retention = StreamConfigRetention.Limits
    )
    {
        var conn = await GetConnectionAsync();
        var js = new NatsJSContext(conn);

        try
        {
            _ = await js.GetStreamAsync(streamName);
            return;
        }
        catch (NatsJSApiException e) when (e.Error.Code == 404 || e.Error.ErrCode == 10059)
        {
            // Create below.
        }

        try
        {
            await js.CreateStreamAsync(
                new StreamConfig
                {
                    Name = streamName,
                    Subjects = [subjectWildcard],
                    Storage = StreamConfigStorage.Memory,
                    Retention = retention,
                }
            );
        }
        catch (NatsJSApiException e) when (e.Error.Code is 400 or 409)
        {
            _ = await js.GetStreamAsync(streamName);
        }
    }

    /// <summary>
    /// Creates the stream an operator would provision for hosts that run with stream provisioning disabled: it captures
    /// every Queue subject under <see cref="OperatorMessageNamePrefix"/>, and keeps acknowledged messages, so a test can
    /// see what was published to it.
    /// </summary>
    public Task EnsureOperatorStreamAsync()
    {
        return EnsureStreamAsync(
            NatsPhysicalAddress.Stream(MessageLane.Queue, OperatorStreamKey),
            NatsPhysicalAddress.Subject(MessageLane.Queue, $"{OperatorMessageNamePrefix}.>"),
            StreamConfigRetention.Limits
        );
    }

    /// <summary>The logical stream name hosts without stream provisioning map every message name to.</summary>
    public const string OperatorStreamKey = "operator-rr";

    /// <summary>The message name prefix that puts a host's subjects under the operator stream.</summary>
    public const string OperatorMessageNamePrefix = "operator";

    /// <summary>
    /// Returns, per stream on the server, the subjects matching <paramref name="subjectFilter"/> that the stream still
    /// stores, omitting streams that store none.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> ListStoredSubjectsAsync(
        string subjectFilter,
        CancellationToken cancellationToken
    )
    {
        var js = new NatsJSContext(await GetConnectionAsync());
        var stored = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);

        await foreach (var stream in js.ListStreamsAsync(cancellationToken: cancellationToken))
        {
            var info = await js.GetStreamAsync(
                stream.Info.Config.Name!,
                new StreamInfoRequest { SubjectsFilter = subjectFilter },
                cancellationToken
            );

            if (info.Info.State.Subjects is { Count: > 0 } subjects)
            {
                stored[stream.Info.Config.Name!] = [.. subjects.Keys];
            }
        }

        return stored;
    }

    public async Task<NatsConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        var opts = NatsOpts.Default with { Url = ConnectionString, ConnectTimeout = TimeSpan.FromSeconds(30) };

        for (var attempt = 1; attempt <= _ConnectionAttempts; attempt++)
        {
            var connection = new NatsConnection(opts);

            try
            {
                await connection.ConnectAsync();
                _connection = connection;

                return connection;
            }
            catch (NatsException) when (attempt == _ConnectionAttempts)
            {
                await connection.DisposeAsync();
                throw;
            }
            catch (NatsException)
            {
                await connection.DisposeAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
        }

        throw new InvalidOperationException("NATS connection attempts were exhausted.");
    }

    public ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        CancellationToken cancellationToken,
        string? streamName = null,
        string? destination = null,
        string? group = null,
        bool createReplacement = true
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Queue,
            streamName,
            destination,
            group,
            createReplacement,
            failEnvelopeBuild: false,
            cancellationToken
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateBusSessionAsync(
        string streamName,
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Bus,
            streamName,
            destination,
            group,
            createReplacement: false,
            failEnvelopeBuild: false,
            cancellationToken
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateLaneSessionAsync(
        MessageLane lane,
        string streamName,
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            lane,
            streamName,
            destination,
            group,
            createReplacement: true,
            failEnvelopeBuild: false,
            cancellationToken
        );
    }

    /// <summary>Opens a session whose consumer the production factory builds from <paramref name="endpoint"/>.</summary>
    public ValueTask<TransportConsumerConformanceSession> CreateEndpointSessionAsync(
        TransportConformanceEndpoint endpoint,
        string streamName,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            endpoint.Lane,
            streamName,
            endpoint.LogicalName,
            endpoint.SubscriptionName,
            createReplacement: false,
            failEnvelopeBuild: false,
            cancellationToken,
            endpoint.ToRequest()
        );
    }

    /// <summary>Counts the client subscriptions the server holds on exactly <paramref name="subject"/>.</summary>
    public async Task<int> CountSubscriptionsAsync(string subject, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync(
            new Uri($"{Container.GetManagementEndpoint().TrimEnd('/')}/connz?subs=1&limit=1024"),
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );

        var count = 0;

        foreach (var connection in document.RootElement.GetProperty("connections").EnumerateArray())
        {
            if (!connection.TryGetProperty("subscriptions_list", out var subscriptions))
            {
                continue;
            }

            foreach (var subscription in subscriptions.EnumerateArray())
            {
                if (string.Equals(subscription.GetString(), subject, StringComparison.Ordinal))
                {
                    count++;
                }
            }
        }

        return count;
    }

    public ValueTask<TransportConsumerConformanceSession> CreateMalformedSessionAsync(
        string streamName,
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Queue,
            streamName,
            destination,
            group,
            createReplacement: true,
            failEnvelopeBuild: true,
            cancellationToken
        );
    }

    private async ValueTask<TransportConsumerConformanceSession> _CreateConformanceSessionAsync(
        MessageLane lane,
        string? streamName,
        string? destination,
        string? group,
        bool createReplacement,
        bool failEnvelopeBuild,
        CancellationToken cancellationToken,
        ConsumerClientRequest? request = null
    )
    {
        streamName ??= $"conf-{Guid.NewGuid():N}"[..29];
        destination ??= $"{streamName}.probe";
        group ??= $"group-{Guid.NewGuid():N}"[..30];

        var services = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(
            new NatsMessagingOptions
            {
                Servers = ConnectionString,
                StreamProvisioning = NatsStreamProvisioning.Reconcile,
                NormalizeStreamName = _ => streamName,
                StreamOptions = config => config.Storage = StreamConfigStorage.Memory,
                ConsumerOptions = config =>
                {
                    config.AckWait = TimeSpan.FromSeconds(1);
                    config.MaxDeliver = 5;
                },
            }
        );
        if (failEnvelopeBuild)
        {
            options.Value.CustomHeadersBuilder = static (_, _, _) =>
                [new KeyValuePair<string, string>(Headers.MessageId, string.Empty)];
        }
#pragma warning disable CA2000 // Ownership transfers to the returned conformance session or the catch cleanup path.
        var pool = new Headless.Messaging.Nats.NatsConnectionPool(
            NullLogger<Headless.Messaging.Nats.NatsConnectionPool>.Instance,
            options
        );
        var producer = new NatsTransport(
            NullLogger<NatsTransport>.Instance,
            pool,
            new Headless.Messaging.Nats.NatsStreamProvisioner(options),
            lane
        );
#pragma warning restore CA2000
        IConsumerClient? consumer = null;

        try
        {
            if (request is null)
            {
#pragma warning disable CA2000 // False positive: the client transfers to consumer, which the session or the catch disposes.
                var client = new NatsConsumerClient(group, 1, options, services, lane: _ToMessageLane(lane));
#pragma warning restore CA2000
                consumer = client;
                await client.ConnectAsync(cancellationToken);
            }
            else
            {
                consumer = await new NatsConsumerClientFactory(options, services).CreateAsync(
                    request,
                    cancellationToken
                );
            }

            var topics = await consumer.FetchMessageNamesAsync([destination], cancellationToken);
            await consumer.SubscribeAsync(topics, cancellationToken);

            return new TransportConsumerConformanceSession(
                destination,
                producer,
                consumer,
                TimeSpan.FromMilliseconds(2_500),
                async () =>
                {
                    await pool.DisposeAsync();
                    await services.DisposeAsync();
                },
                createReplacementSession: createReplacement
                    ? replacementToken =>
                        _CreateConformanceSessionAsync(
                            lane,
                            streamName,
                            destination,
                            group,
                            createReplacement: false,
                            failEnvelopeBuild,
                            replacementToken
                        )
                    : null
            );
        }
        catch
        {
            if (consumer is not null)
            {
                await consumer.DisposeAsync();
            }

            await pool.DisposeAsync();
            await services.DisposeAsync();
            throw;
        }
    }

    private static MessageLane _ToMessageLane(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => MessageLane.Bus,
            MessageLane.Queue => MessageLane.Queue,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null),
        };

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }
}

[CollectionDefinition("Nats", DisableParallelization = true)]
public sealed class NatsCollection : ICollectionFixture<NatsFixture>;
