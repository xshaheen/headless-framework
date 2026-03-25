// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Testcontainers.Nats;

namespace Tests;

[UsedImplicitly]
public sealed class NatsFixture : IAsyncLifetime
{
    private readonly NatsContainer _container = new NatsBuilder("nats:2-alpine").Build();

    private NatsConnection? _connection;

    /// <summary>Gets the NATS connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
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
            await js.CreateStreamAsync(
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

        var opts = NatsOpts.Default with { Url = ConnectionString };
        _connection = new NatsConnection(opts);
        await _connection.ConnectAsync();
        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Nats", DisableParallelization = true)]
public sealed class NatsCollection : ICollectionFixture<NatsFixture>;
