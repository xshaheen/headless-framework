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
        await _CreateQueueAsync(queueName, cancellationToken);

        try
        {
            return await _CreateSessionAsync(
                IntentType.Queue,
                queueName,
                $"group-{Guid.NewGuid():N}",
                AzureServiceBusMessagingOptions.DefaultTopicPath,
                async () => await _DeleteQueueAsync(queueName, CancellationToken.None),
                cancellationToken
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
            IntentType.Bus,
            messageName,
            subscriptionName,
            topicName,
            disposeEntity: null,
            cancellationToken
        );
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var queueName in _queues.Keys)
        {
            await _DeleteQueueAsync(queueName, CancellationToken.None);
        }

        foreach (var topicName in _topics.Keys)
        {
            await _DeleteTopicAsync(topicName, CancellationToken.None);
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask<TransportConsumerConformanceSession> _CreateSessionAsync(
        IntentType intent,
        string destination,
        string group,
        string topicPath,
        Func<ValueTask>? disposeEntity,
        CancellationToken cancellationToken
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
                intent == IntentType.Queue
                    ? (ITransport)serviceProvider.GetRequiredService<IQueueTransport>()
                    : serviceProvider.GetRequiredService<IBusTransport>();
            var factory = serviceProvider.GetRequiredService<IConsumerClientFactory>();
            var consumer = await ((IIntentAwareConsumerClientFactory)factory).CreateAsync(
                group,
                2,
                intent,
                cancellationToken
            );
            consumer.OnLogCallback = _ => { };

            try
            {
                await consumer.SubscribeAsync([destination], cancellationToken);

                return new TransportConsumerConformanceSession(
                    destination,
                    producer,
                    consumer,
                    TimeSpan.FromSeconds(3),
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
                    }
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
        if (!_queues.TryRemove(queueName, out _) || _administrationClient is null)
        {
            return;
        }

        try
        {
            await _administrationClient.DeleteQueueAsync(queueName, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 404) { }
    }

    private async ValueTask _DeleteTopicAsync(string topicName, CancellationToken cancellationToken)
    {
        if (!_topics.TryRemove(topicName, out _) || _administrationClient is null)
        {
            return;
        }

        try
        {
            await _administrationClient.DeleteTopicAsync(topicName, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 404) { }
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
