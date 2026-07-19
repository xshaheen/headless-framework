// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Tests;

/// <summary>
/// Collection fixture providing a RabbitMQ container for integration tests.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class RabbitMqFixture : HeadlessRabbitMqFixture, ICollectionFixture<RabbitMqFixture>
{
    private IConnection? _connection;

    /// <summary>Gets the RabbitMQ connection string.</summary>
    public string ConnectionString => Container.GetConnectionString();

    /// <summary>Gets the RabbitMQ hostname.</summary>
    public string HostName => Container.Hostname;

    /// <summary>Gets the RabbitMQ port.</summary>
    public int Port => Container.GetMappedPublicPort(5672);

    /// <summary>Gets the RabbitMQ username.</summary>
    public string UserName => RabbitMqBuilder.DefaultUsername;

    /// <summary>Gets the RabbitMQ password.</summary>
    public string Password => RabbitMqBuilder.DefaultPassword;

    /// <summary>Gets or creates a shared connection to RabbitMQ.</summary>
    public async Task<IConnection> GetConnectionAsync()
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        var factory = new ConnectionFactory
        {
            HostName = HostName,
            Port = Port,
            UserName = UserName,
            Password = Password,
        };

        _connection = await factory.CreateConnectionAsync();
        return _connection;
    }

    public async ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool createReplacement = true
    )
    {
        destination ??= $"conf-{Guid.NewGuid():N}";
        group ??= $"group-{Guid.NewGuid():N}";
        var services = new ServiceCollection().BuildServiceProvider();
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        var rabbitOptions = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = HostName,
                Port = Port,
                UserName = UserName,
                Password = Password,
                ExchangeName = $"conf-{Guid.NewGuid():N}",
            }
        );

#pragma warning disable CA2000 // Ownership transfers to the returned conformance session or the catch cleanup path.
        var pool = new ConnectionChannelPool(
            NullLogger<ConnectionChannelPool>.Instance,
            messagingOptions,
            rabbitOptions
        );
        var producer = new RabbitMqTransport(NullLogger<RabbitMqTransport>.Instance, pool);
        var consumer = new RabbitMqConsumerClient(
            group,
            1,
            pool,
            rabbitOptions,
            services,
            intentType: IntentType.Queue
        );
#pragma warning restore CA2000

        try
        {
            await consumer.SubscribeAsync([destination], cancellationToken);

            return new TransportConsumerConformanceSession(
                destination,
                producer,
                consumer,
                TimeSpan.FromMilliseconds(1_500),
                async () =>
                {
                    await pool.DisposeAsync();
                    await services.DisposeAsync();
                },
                createReplacementSession: createReplacement
                    ? replacementToken =>
                        CreateConformanceSessionAsync(replacementToken, destination, group, createReplacement: false)
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

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        await base.DisposeAsyncCore();
    }
}
