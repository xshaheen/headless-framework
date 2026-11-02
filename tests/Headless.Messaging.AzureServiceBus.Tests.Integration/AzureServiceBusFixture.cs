// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure;
using Azure.Messaging.ServiceBus;
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

    public async ValueTask<TransportConsumerConformanceSession> CreateRoutingAffinitySessionAsync(
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        await _CreateQueueAsync(destination, cancellationToken, requiresSession: true);
        return await _CreateSessionAsync(
            MessageLane.Queue,
            destination,
            group,
            AzureServiceBusMessagingOptions.DefaultTopicPath,
            async () => await _DeleteQueueAsync(destination, CancellationToken.None),
            cancellationToken,
            enableSessions: true
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateQueueSessionAsync(CancellationToken cancellationToken)
    {
        return CreateQueueSessionAsync(new TransportConformanceSessionOptions(), cancellationToken);
    }

    /// <summary>
    /// Opens a session on a queue of its own whose max delivery count is <see cref="TransportConformanceSessionOptions.DeliveryLimit"/>,
    /// with the session's header builder installed. Service Bus always dead-letters a message that spends its delivery
    /// count, so <see cref="TransportConformanceSessionOptions.DeadLettering"/> needs nothing more.
    /// </summary>
    public async ValueTask<TransportConsumerConformanceSession> CreateQueueSessionAsync(
        TransportConformanceSessionOptions options,
        CancellationToken cancellationToken
    )
    {
        RequireSupportedSessionOptions(options);
        var queueName = _UniqueName("queue");
        var group = $"group-{Guid.NewGuid():N}";
        await _CreateQueueAsync(queueName, cancellationToken, maxDeliveryCount: options.DeliveryLimit ?? 10);

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
                        createReplacementSession: null,
                        sessionOptions: options
                    ),
                sessionOptions: options
            );
        }
        catch
        {
            await _DeleteQueueAsync(queueName, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Creates a queue with a short lock and no sessions, deleted when the fixture is disposed.</summary>
    public async ValueTask CreateQueueAsync(string queueName, CancellationToken cancellationToken)
    {
        await _CreateQueueAsync(queueName, cancellationToken);
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

    public async ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        TransportConformanceEndpoint endpoint,
        string topicName,
        bool ownsEntity,
        CancellationToken cancellationToken
    )
    {
        var lane = endpoint.Lane;
        var destination = endpoint.LogicalName;
        var group = endpoint.SubscriptionName;
        var request = endpoint.ToRequest(concurrency: 2);

        if (lane == MessageLane.Queue)
        {
            if (ownsEntity)
            {
                await _CreateQueueAsync(destination, cancellationToken);
            }

            return await _CreateSessionAsync(
                lane,
                destination,
                group,
                AzureServiceBusMessagingOptions.DefaultTopicPath,
                ownsEntity ? async () => await _DeleteQueueAsync(destination, CancellationToken.None) : null,
                cancellationToken
            );
        }

        if (lane != MessageLane.Bus)
        {
            throw new ArgumentOutOfRangeException(nameof(endpoint), lane, null);
        }

        // An every-instance session creates, and on dispose deletes, a subscription of its own.
        if (request.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            return await _CreateSessionAsync(
                lane,
                destination,
                group,
                topicName,
                disposeEntity: null,
                cancellationToken,
                request: request
            );
        }

        if (ownsEntity)
        {
            // The factory maps the consumer identity to its subscription name, so pre-provision under that name.
            var options = new CreateSubscriptionOptions(
                topicName,
                AzureServiceBusConsumerClientFactory.BusSubscriptionName(group)
            )
            {
                LockDuration = TimeSpan.FromSeconds(5),
                MaxDeliveryCount = 10,
            };
            await _RequireAdministrationClient()
                .CreateSubscriptionAsync(options, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _CreateSessionAsync(lane, destination, group, topicName, disposeEntity: null, cancellationToken);
    }

    /// <summary>
    /// Takes every message the dead-letter subqueue of the session's queue holds, as an operator draining it would, and
    /// returns their message ids.
    /// </summary>
    public ValueTask<IReadOnlyList<string>> TakeDeadLetteredMessageIdsAsync(
        TransportConsumerConformanceSession session,
        CancellationToken cancellationToken
    )
    {
        return TakeDeadLetteredMessageIdsAsync(_RequireConnectionString(), session.Destination, cancellationToken);
    }

    /// <summary>Returns the subscription on <paramref name="topicName"/>, or <see langword="null"/> when none exists.</summary>
    public async ValueTask<SubscriptionProperties?> GetSubscriptionOrDefaultAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken
    )
    {
        var administrationClient = _RequireAdministrationClient();

        if (!await administrationClient.SubscriptionExistsAsync(topicName, subscriptionName, cancellationToken))
        {
            return null;
        }

        return await administrationClient.GetSubscriptionAsync(topicName, subscriptionName, cancellationToken);
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

        await AzureServiceBusResourceCleanup.DeleteAllAsync(operations);
    }

    private ValueTask<TransportConsumerConformanceSession> _CreateSessionAsync(
        MessageLane lane,
        string destination,
        string group,
        string topicPath,
        Func<ValueTask>? disposeEntity,
        CancellationToken cancellationToken,
        Func<CancellationToken, ValueTask<TransportConsumerConformanceSession>>? createReplacementSession = null,
        bool enableSessions = false,
        ConsumerClientRequest? request = null,
        TransportConformanceSessionOptions? sessionOptions = null
    )
    {
        return CreateSessionAsync(
            _RequireConnectionString(),
            lane,
            destination,
            group,
            topicPath,
            disposeEntity,
            cancellationToken,
            createReplacementSession,
            enableSessions,
            request,
            sessionOptions
        );
    }

    /// <summary>
    /// Fails for an option the Azure Service Bus leaves cannot configure on a session; the delivery limit is the queue's,
    /// so each fixture checks it against the queue it hands out.
    /// </summary>
    internal static void RequireSupportedSessionOptions(TransportConformanceSessionOptions options)
    {
        if (
            options with
            {
                HeaderBuilder = null,
                DeliveryLimit = null,
                DeadLettering = false,
            } != new TransportConformanceSessionOptions()
        )
        {
            throw new NotSupportedException($"The Azure Service Bus conformance leaves do not configure {options}.");
        }
    }

    /// <summary>
    /// Takes every message the dead-letter subqueue of <paramref name="queueName"/> holds and returns their message ids,
    /// so one message dead-lettered twice shows twice.
    /// </summary>
    /// <remarks>
    /// The drain reads in peek-lock mode, not <see cref="ServiceBusReceiveMode.ReceiveAndDelete"/>: a receive-and-delete
    /// link is settled on send, so a message the broker pushes onto a receiver's outstanding credit while it closes would
    /// be deleted without ever being returned, and a polling caller would wait for a message that no longer exists. A copy
    /// whose lock is lost before its completion is not taken; it stays in the subqueue and a later read returns it.
    /// </remarks>
    internal static async ValueTask<IReadOnlyList<string>> TakeDeadLetteredMessageIdsAsync(
        string connectionString,
        string queueName,
        CancellationToken cancellationToken
    )
    {
        await using var client = new ServiceBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            queueName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }
        );
        var taken = new List<string>();

        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(
                maxMessages: 50,
                maxWaitTime: TimeSpan.FromSeconds(1),
                cancellationToken
            );

            if (messages.Count == 0)
            {
                return taken;
            }

            foreach (var message in messages)
            {
                if (await _TryCompleteAsync(receiver, message, cancellationToken))
                {
                    taken.Add(message.MessageId);
                }
            }
        }
    }

    /// <summary>
    /// Completes <paramref name="message"/>, or returns <see langword="false"/> when its lock is gone, so the caller treats
    /// the copy as not taken yet instead of failing the read.
    /// </summary>
    private static async ValueTask<bool> _TryCompleteAsync(
        ServiceBusReceiver receiver,
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await receiver.CompleteMessageAsync(message, cancellationToken);
            return true;
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessageLockLost)
        {
            // Nothing else settles this subqueue, so a lost lock means it expired or an earlier poll's closed receiver
            // still holds it: the copy is still in the subqueue, and the read after the lock lapses takes it.
            return false;
        }
    }

    /// <summary>
    /// Opens a conformance session on entities that already exist under <paramref name="connectionString"/>; the real
    /// namespace and the emulator share it, so both profiles drive the provider through the same wiring.
    /// </summary>
    internal static async ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        string connectionString,
        MessageLane lane,
        string destination,
        string group,
        string topicPath,
        Func<ValueTask>? disposeEntity,
        CancellationToken cancellationToken,
        Func<CancellationToken, ValueTask<TransportConsumerConformanceSession>>? createReplacementSession = null,
        bool enableSessions = false,
        ConsumerClientRequest? request = null,
        TransportConformanceSessionOptions? sessionOptions = null
    )
    {
        request ??= new ConsumerClientRequest(group, 2, lane);
        var headerBuilder = sessionOptions?.HeaderBuilder;

        // Every-instance subscriptions are created by the consumer itself, which takes AutoProvision.
        var autoProvision = request.Kind is ConsumerSubscriptionKind.EveryInstance;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                options.TopicPath = topicPath;
                options.AutoProvision = autoProvision;
                options.EnableSessions = enableSessions;
                if (enableSessions)
                {
                    options.CustomHeadersBuilder = static (message, _) =>
                        [new("conformance-native-session", message.SessionId)];
                }
                else if (headerBuilder is not null)
                {
                    options.CustomHeadersBuilder = (message, _) =>
                    {
                        headerBuilder(message.MessageId);
                        return [];
                    };
                }

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
            var consumer = await factory.CreateAsync(request, cancellationToken);
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

    private async Task _CreateQueueAsync(
        string queueName,
        CancellationToken cancellationToken,
        bool requiresSession = false,
        int maxDeliveryCount = 10
    )
    {
        var options = new CreateQueueOptions(queueName)
        {
            RequiresSession = requiresSession,
            LockDuration = TimeSpan.FromSeconds(5),
            MaxDeliveryCount = maxDeliveryCount,
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
