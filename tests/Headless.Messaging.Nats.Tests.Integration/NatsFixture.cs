// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using System.Text;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Testcontainers.Nats;

namespace Tests;

[UsedImplicitly]
public sealed class NatsFixture : IAsyncLifetime
{
    private static readonly byte[] _NatsConfig = Encoding.UTF8.GetBytes(
        """
        port: 4222
        monitor_port: 8222
        jetstream {}
        """
    );

    private readonly NatsContainer _container = new NatsBuilder("nats:2-alpine")
        .WithResourceMapping(_NatsConfig, "/etc/nats/nats-server.conf")
        .Build();

    private NatsConnection? _connection;

    /// <summary>Gets the NATS connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public NatsOpts NatsOpts =>
        NatsOpts.Default with
        {
            Url = ConnectionString,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Wait for NATS protocol to be responsive (not just TCP port open).
        // Docker Desktop on macOS can have port forwarding latency.
        var uri = new Uri(ConnectionString);
        await _WaitForNatsProtocolAsync(uri.Host, uri.Port, TimeSpan.FromSeconds(15));
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
                    NoAck = subjectWildcard == ">", // catch-all wildcard requires NoAck
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

        _connection = new NatsConnection(NatsOpts);
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

    /// <summary>
    /// Waits until the NATS server sends its INFO protocol message over TCP.
    /// This confirms the server is fully ready for connections, not just TCP-listening.
    /// </summary>
    private static async Task _WaitForNatsProtocolAsync(string host, int port, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port, cts.Token);
                await using var stream = tcp.GetStream();
                stream.ReadTimeout = 5000;
                var buffer = new byte[512];
                var bytesRead = await stream.ReadAsync(buffer, cts.Token);

                if (bytesRead > 0 && Encoding.UTF8.GetString(buffer, 0, bytesRead).StartsWith("INFO"))
                {
                    return; // Server is ready
                }
            }
            catch (Exception) when (!cts.IsCancellationRequested)
            {
                await Task.Delay(200, cts.Token);
            }
        }

        throw new TimeoutException($"NATS server at {host}:{port} did not respond with INFO protocol within {timeout}");
    }
}

[CollectionDefinition("Nats", DisableParallelization = true)]
public sealed class NatsCollection : ICollectionFixture<NatsFixture>;
