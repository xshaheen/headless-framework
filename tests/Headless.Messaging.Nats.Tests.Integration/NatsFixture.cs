// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Testcontainers.Nats;

namespace Tests;

[UsedImplicitly]
public sealed class NatsFixture : HeadlessNatsFixture
{
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
        _connection = new NatsConnection(opts);
        await _connection.ConnectAsync();
        return _connection;
    }

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
