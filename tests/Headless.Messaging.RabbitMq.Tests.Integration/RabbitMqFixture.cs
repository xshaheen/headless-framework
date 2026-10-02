// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
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

    /// <summary>
    /// Deletes <paramref name="queueName"/> the way an operator does, from the broker node rather than an AMQP
    /// connection, which an exclusive queue would refuse.
    /// </summary>
    public async Task DeleteQueueAsOperatorAsync(string queueName, CancellationToken cancellationToken)
    {
        var result = await Container.ExecAsync(["rabbitmqctl", "delete_queue", queueName], cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"rabbitmqctl delete_queue {queueName} failed ({result.ExitCode}): {result.Stderr}"
            );
        }
    }

    /// <summary>
    /// Lists the default virtual host's queues as the broker node reports them, the way an operator inspects them, so
    /// a test sees queues that belong to other connections, exclusive ones included.
    /// </summary>
    public async Task<IReadOnlyList<RabbitMqBrokerQueue>> ListQueuesAsOperatorAsync(CancellationToken cancellationToken)
    {
        var result = await Container.ExecAsync(
            ["rabbitmqctl", "list_queues", "-q", "--no-table-headers", "name", "durable", "exclusive"],
            cancellationToken
        );

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"rabbitmqctl list_queues failed ({result.ExitCode}): {result.Stderr}");
        }

        return result
            .Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Split('\t'))
            .Select(static columns => new RabbitMqBrokerQueue(
                columns[0],
                Durable: bool.Parse(columns[1]),
                Exclusive: bool.Parse(columns[2])
            ))
            .ToArray();
    }

    /// <summary>
    /// Returns whether the broker holds <paramref name="queueName"/>, by a passive declare on a channel of its own. An
    /// exclusive queue of another connection answers with <c>RESOURCE_LOCKED</c> rather than its details, which still
    /// proves it exists.
    /// </summary>
    public async ValueTask<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
            return true;
        }
        catch (OperationInterruptedException e) when (e.ShutdownReason?.ReplyCode == Constants.NotFound)
        {
            return false;
        }
        catch (OperationInterruptedException e) when (e.ShutdownReason?.ReplyCode == Constants.ResourceLocked)
        {
            return true;
        }
    }

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

    public ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool createReplacement = true,
        string? exchangeName = null
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Queue,
            destination,
            group,
            exchangeName,
            createReplacement,
            failEnvelopeBuild: false,
            cancellationToken
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateBusSessionAsync(
        string exchangeName,
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Bus,
            destination,
            group,
            exchangeName,
            createReplacement: false,
            failEnvelopeBuild: false,
            cancellationToken
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateLaneSessionAsync(
        MessageLane lane,
        string exchangeName,
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            lane,
            destination,
            group,
            exchangeName,
            createReplacement: true,
            failEnvelopeBuild: false,
            cancellationToken
        );
    }

    /// <summary>Opens a session whose consumer the production factory builds from <paramref name="endpoint"/>.</summary>
    public ValueTask<TransportConsumerConformanceSession> CreateEndpointSessionAsync(
        TransportConformanceEndpoint endpoint,
        string exchangeName,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            endpoint.Lane,
            endpoint.LogicalName,
            endpoint.SubscriptionName,
            exchangeName,
            createReplacement: false,
            failEnvelopeBuild: false,
            cancellationToken,
            endpoint.ToRequest()
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateMalformedSessionAsync(
        string exchangeName,
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Queue,
            destination,
            group,
            exchangeName,
            createReplacement: true,
            failEnvelopeBuild: true,
            cancellationToken
        );
    }

    private async ValueTask<TransportConsumerConformanceSession> _CreateConformanceSessionAsync(
        MessageLane lane,
        string? destination,
        string? group,
        string? exchangeName,
        bool createReplacement,
        bool failEnvelopeBuild,
        CancellationToken cancellationToken,
        ConsumerClientRequest? request = null
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
                ExchangeName = exchangeName ?? $"conf-{Guid.NewGuid():N}",
            }
        );
        if (failEnvelopeBuild)
        {
            rabbitOptions.Value.CustomHeadersBuilder = static (_, _) =>
                [new KeyValuePair<string, string>(Headless.Messaging.Headers.MessageId, string.Empty)];
        }

#pragma warning disable CA2000 // Ownership transfers to the returned conformance session or the catch cleanup path.
        var pool = new ConnectionChannelPool(
            NullLogger<ConnectionChannelPool>.Instance,
            messagingOptions,
            rabbitOptions
        );
        var producer = new RabbitMqTransport(NullLogger<RabbitMqTransport>.Instance, pool, lane);
#pragma warning restore CA2000
        IConsumerClient? consumer = null;

        try
        {
#pragma warning disable CA2000 // False positive: consumer transfers to the returned session or the catch disposes it.
            consumer = request is null
                ? new RabbitMqConsumerClient(group, 1, pool, rabbitOptions, services, lane: _ToMessageLane(lane))
                : await new RabbitMqConsumerClientFactory(rabbitOptions, pool, services).CreateAsync(
                    request,
                    cancellationToken
                );
#pragma warning restore CA2000
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
                        _CreateConformanceSessionAsync(
                            lane,
                            destination,
                            group,
                            rabbitOptions.Value.ExchangeName,
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
            _connection = null;
        }

        await base.DisposeAsyncCore();
    }
}

/// <summary>A queue as <c>rabbitmqctl list_queues</c> reports it.</summary>
public sealed record RabbitMqBrokerQueue(string Name, bool Durable, bool Exclusive);
