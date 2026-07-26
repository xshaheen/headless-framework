// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
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
    public async Task EnsureStreamAsync(string streamName, string subjectWildcard)
    {
        var conn = await GetConnectionAsync();
        var js = new NatsJSContext(conn);

        try
        {
            await js.CreateOrUpdateStreamAsync(
                new StreamConfig
                {
                    Name = streamName,
                    Subjects = [subjectWildcard],
                    Storage = StreamConfigStorage.Memory,
                }
            );
        }
        catch (NatsJSApiException e) when (e.Error.Code == 409)
        {
            // Already exists
        }
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
            cancellationToken
        );
    }

    private async ValueTask<TransportConsumerConformanceSession> _CreateConformanceSessionAsync(
        MessageLane lane,
        string? streamName,
        string? destination,
        string? group,
        bool createReplacement,
        CancellationToken cancellationToken
    )
    {
        streamName ??= $"conf-{Guid.NewGuid():N}"[..29];
        destination ??= $"{streamName}.probe";
        group ??= $"group-{Guid.NewGuid():N}"[..30];
        await EnsureStreamAsync(streamName, $"{streamName}.>");

        var services = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(
            new NatsMessagingOptions
            {
                Servers = ConnectionString,
                EnableSubscriberClientStreamAndSubjectCreation = false,
                ConsumerOptions = config =>
                {
                    config.AckWait = TimeSpan.FromSeconds(1);
                    config.MaxDeliver = 5;
                },
            }
        );
#pragma warning disable CA2000 // Ownership transfers to the returned conformance session or the catch cleanup path.
        var pool = new Headless.Messaging.Nats.NatsConnectionPool(
            NullLogger<Headless.Messaging.Nats.NatsConnectionPool>.Instance,
            options
        );
        var producer = new NatsTransport(NullLogger<NatsTransport>.Instance, pool);
        var consumer = new NatsConsumerClient(group, 1, options, services, intentType: _ToIntentType(lane));
#pragma warning restore CA2000

        try
        {
            await consumer.ConnectAsync(cancellationToken);
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
                            replacementToken
                        )
                    : null
            );
        }
        catch
        {
            await consumer.DisposeAsync();
            await pool.DisposeAsync();
            await services.DisposeAsync();
            throw;
        }
    }

    private static IntentType _ToIntentType(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => IntentType.Bus,
            MessageLane.Queue => IntentType.Queue,
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
