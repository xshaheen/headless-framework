// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[UsedImplicitly]
public sealed class AzureServiceBusFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable = "HEADLESS_TEST_AZURE_SERVICE_BUS_CONNECTION_STRING";

    private readonly ConcurrentDictionary<string, byte> _queues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _topics = new(StringComparer.Ordinal);
    private readonly string? _connectionString = Environment.GetEnvironmentVariable(
        ConnectionStringEnvironmentVariable
    );
    private ServiceBusAdministrationClient? _administrationClient;

    public string ConnectionString => _RequireConnectionString();

    public ValueTask InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            _administrationClient = new ServiceBusAdministrationClient(_connectionString);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<TransportConsumerConformanceSession> CreateQueueSessionAsync(
        CancellationToken cancellationToken
    )
    {
        var queueName = _UniqueName("queue");
        var group = $"group-{Guid.NewGuid():N}";
        await _CreateQueueAsync(queueName, cancellationToken);

        try
        {
            return await _CreateSessionAsync(
                MessageLane.Queue,
                queueName,
                group,
                AzureServiceBusMessagingOptions.DefaultTopicPath,
                async () => await _DeleteQueueAsync(queueName, CancellationToken.None),
                cancellationToken,
                replacementToken =>
                    _CreateSessionAsync(
                        MessageLane.Queue,
                        queueName,
                        group,
                        AzureServiceBusMessagingOptions.DefaultTopicPath,
                        disposeEntity: null,
                        cancellationToken: replacementToken,
                        createReplacementSession: null
                    )
            );
        }
        catch
        {
            await _DeleteQueueAsync(queueName, CancellationToken.None);
            throw;
        }
    }

    public async ValueTask<string> CreateTopicAsync(CancellationToken cancellationToken)
    {
        var topicName = _UniqueName("topic");
        var administrationClient = _RequireAdministrationClient();
        await administrationClient.CreateTopicAsync(topicName, cancellationToken).ConfigureAwait(false);
        _topics.TryAdd(topicName, 0);
        return topicName;
    }

    public async ValueTask<TransportConsumerConformanceSession> CreateBusSessionAsync(
        string topicName,
        string messageName,
        CancellationToken cancellationToken
    )
    {
        var subscriptionName = _UniqueName("sub");
        var administrationClient = _RequireAdministrationClient();
        var options = new CreateSubscriptionOptions(topicName, subscriptionName)
        {
            LockDuration = TimeSpan.FromSeconds(5),
            MaxDeliveryCount = 10,
        };
        await administrationClient.CreateSubscriptionAsync(options, cancellationToken).ConfigureAwait(false);

        return await _CreateSessionAsync(
            MessageLane.Bus,
            messageName,
            subscriptionName,
            topicName,
            disposeEntity: null,
            cancellationToken
        );
    }

    public async ValueTask DisposeAsync()
    {
        var operations = _queues
            .Keys.Select(queueName =>
                (
                    Resource: $"Azure Service Bus queue '{queueName}'",
                    Delete: (Func<ValueTask>)(() => _DeleteQueueAsync(queueName, CancellationToken.None))
                )
            )
            .Concat(
                _topics.Keys.Select(topicName =>
                    (
                        Resource: $"Azure Service Bus topic '{topicName}'",
                        Delete: (Func<ValueTask>)(() => _DeleteTopicAsync(topicName, CancellationToken.None))
                    )
                )
            )
            .ToArray();

        GC.SuppressFinalize(this);
        await AzureServiceBusResourceCleanup.DeleteAllAsync(operations);
    }

    private async ValueTask<TransportConsumerConformanceSession> _CreateSessionAsync(
        MessageLane lane,
        string destination,
        string group,
        string topicPath,
        Func<ValueTask>? disposeEntity,
        CancellationToken cancellationToken,
        Func<CancellationToken, ValueTask<TransportConsumerConformanceSession>>? createReplacementSession = null
    )
    {
        var connectionString = _RequireConnectionString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                options.TopicPath = topicPath;
                options.AutoProvision = false;
                options.MaxConcurrentCalls = 2;
                options.MaxAutoLockRenewalDuration = TimeSpan.Zero;
            })
        );
        var serviceProvider = services.BuildServiceProvider();

        try
        {
            var producer =
                lane == MessageLane.Queue
                    ? (ITransport)serviceProvider.GetRequiredService<IQueueTransport>()
                    : serviceProvider.GetRequiredService<IBusTransport>();
            var factory = serviceProvider.GetRequiredService<IConsumerClientFactory>();
            var consumer = await factory.CreateAsync(group, 2, lane, cancellationToken);
            consumer.AttachCallbacks(onMessage: null, onLog: _ => { });

            try
            {
                await consumer.SubscribeAsync([destination], cancellationToken);

                return new TransportConsumerConformanceSession(
                    destination,
                    producer,
                    consumer,
                    TimeSpan.FromSeconds(8),
                    async () =>
                    {
                        try
                        {
                            await serviceProvider.DisposeAsync();
                        }
                        finally
                        {
                            if (disposeEntity is not null)
                            {
                                await disposeEntity();
                            }
                        }
                    },
                    createReplacementSession: createReplacementSession
                );
            }
            catch
            {
                await consumer.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await serviceProvider.DisposeAsync();
            throw;
        }
    }

    private async Task _CreateQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        var options = new CreateQueueOptions(queueName)
        {
            LockDuration = TimeSpan.FromSeconds(5),
            MaxDeliveryCount = 10,
        };
        await _RequireAdministrationClient().CreateQueueAsync(options, cancellationToken).ConfigureAwait(false);
        _queues.TryAdd(queueName, 0);
    }

    private async ValueTask _DeleteQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        if (_administrationClient is null)
        {
            return;
        }

        await AzureServiceBusResourceCleanup.DeleteTrackedAsync(
            _queues,
            queueName,
            async token => await _administrationClient.DeleteQueueAsync(queueName, token).ConfigureAwait(false),
            exception => exception is RequestFailedException { Status: 404 },
            cancellationToken
        );
    }

    private async ValueTask _DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
    {
        if (_administrationClient is null)
        {
            return;
        }

        await AzureServiceBusResourceCleanup.DeleteTrackedAsync(
            _topics,
            topicName,
            async token => await _administrationClient.DeleteTopicAsync(topicName, token).ConfigureAwait(false),
            exception => exception is RequestFailedException { Status: 404 },
            cancellationToken
        );
    }

    private string _RequireConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            Assert.Skip(
                $"Azure Service Bus real-service tests require {ConnectionStringEnvironmentVariable}; no namespace credential was provided."
            );
        }

        return _connectionString!;
    }

    private ServiceBusAdministrationClient _RequireAdministrationClient()
    {
        _ = _RequireConnectionString();
        return _administrationClient!;
    }

    private static string _UniqueName(string kind)
    {
        return $"hfconf-{kind}-{Guid.NewGuid():N}";
    }
}

[CollectionDefinition("AzureServiceBus", DisableParallelization = true)]
public sealed class AzureServiceBusCollection : ICollectionFixture<AzureServiceBusFixture>;
