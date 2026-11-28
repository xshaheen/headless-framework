// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Messaging.AzureServiceBus;

namespace Tests;

/// <summary>
/// An in-memory Service Bus namespace behind a substituted <see cref="ServiceBusAdministrationClient"/> and
/// <see cref="ServiceBusClient"/>: topics, subscriptions with their rules, queues, and the messages each entity and its
/// dead-letter subqueue hold. Every administration write is recorded, so a test can prove a read-only path wrote nothing.
/// </summary>
internal sealed class FakeServiceBusNamespace
{
    private const string _DeadLetterSuffix = "/$deadletterqueue";

    public ServiceBusAdministrationClient Administration { get; } = Substitute.For<ServiceBusAdministrationClient>();

    public ServiceBusClient Client { get; } = Substitute.For<ServiceBusClient>();

    public IAzureServiceBusClientPool Pool { get; } = Substitute.For<IAzureServiceBusClientPool>();

    public HashSet<string> Topics { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Queues { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each subscription's rules, by rule name, keyed by <c>topic/subscription</c>.</summary>
    public Dictionary<string, Dictionary<string, RuleFilter>> Subscriptions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The messages waiting in each entity path, dead-letter subqueues under their own path.</summary>
    public Dictionary<string, int> Messages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every administration call that creates, changes, or deletes an entity or a rule, in order.</summary>
    public List<string> Writes { get; } = [];

    public FakeServiceBusNamespace()
    {
        Pool.GetAdministrationClient().Returns(Administration);
        Pool.GetClient().Returns(Client);
        Client
            .CreateProcessor(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusProcessorOptions>())
            .Returns(_ => Substitute.For<ServiceBusProcessor>());
        Client
            .CreateProcessor(Arg.Any<string>(), Arg.Any<ServiceBusProcessorOptions>())
            .Returns(_ => Substitute.For<ServiceBusProcessor>());

        _StubTopics();
        _StubSubscriptions();
        _StubRules();
        _StubQueues();
        _StubReceivers();
    }

    public static string Key(string topic, string subscription) => $"{topic}/{subscription}";

    public static string DeadLetter(string path) => path + _DeadLetterSuffix;

    public void AddSubscription(string topic, string subscription, params string[] rules) =>
        Subscriptions[Key(topic, subscription)] = rules.ToDictionary(
            static rule => rule,
            static RuleFilter (_) => new TrueRuleFilter(),
            StringComparer.Ordinal
        );

    private static ServiceBusException _NotFound(string entity) =>
        new($"'{entity}' not found", ServiceBusFailureReason.MessagingEntityNotFound);

    private static Response<T> _Response<T>(T value) => Response.FromValue(value, Substitute.For<Response>());

    private static AsyncPageable<T> _Pageable<T>(IEnumerable<T> values)
        where T : notnull =>
        AsyncPageable<T>.FromPages([
            Page<T>.FromValues([.. values], continuationToken: null, Substitute.For<Response>()),
        ]);

    private void _StubTopics()
    {
        Administration
            .TopicExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_Response(Topics.Contains(call.ArgAt<string>(0)))));
        Administration
            .CreateTopicAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                Writes.Add($"create topic {name}");
                Topics.Add(name);
                return Task.FromResult<Response<TopicProperties>>(null!);
            });
        Administration
            .GetTopicsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
                _Pageable(
                    Topics.Select(static name =>
                        ServiceBusModelFactory.TopicProperties(
                            name,
                            maxSizeInMegabytes: 1024,
                            requiresDuplicateDetection: false,
                            defaultMessageTimeToLive: TimeSpan.MaxValue,
                            autoDeleteOnIdle: TimeSpan.MaxValue,
                            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(1),
                            enableBatchedOperations: true,
                            status: EntityStatus.Active,
                            enablePartitioning: false
                        )
                    )
                )
            );
    }

    private void _StubSubscriptions()
    {
        Administration
            .SubscriptionExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult(_Response(Subscriptions.ContainsKey(Key(call.ArgAt<string>(0), call.ArgAt<string>(1)))))
            );
        Administration
            .CreateSubscriptionAsync(
                Arg.Any<CreateSubscriptionOptions>(),
                Arg.Any<CreateRuleOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var options = call.Arg<CreateSubscriptionOptions>();
                var rule = call.Arg<CreateRuleOptions>();
                _CreateSubscription(options);
                Subscriptions[Key(options.TopicName, options.SubscriptionName)][rule.Name] = rule.Filter;
                return Task.FromResult<Response<SubscriptionProperties>>(null!);
            });
        Administration
            .CreateSubscriptionAsync(Arg.Any<CreateSubscriptionOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var options = call.Arg<CreateSubscriptionOptions>();
                _CreateSubscription(options);
                Subscriptions[Key(options.TopicName, options.SubscriptionName)][RuleProperties.DefaultRuleName] =
                    new TrueRuleFilter();
                return Task.FromResult<Response<SubscriptionProperties>>(null!);
            });
        Administration
            .GetSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var prefix = call.ArgAt<string>(0) + "/";
                return _Pageable(
                    Subscriptions
                        .Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(key =>
                            ServiceBusModelFactory.SubscriptionProperties(
                                call.ArgAt<string>(0),
                                key[prefix.Length..],
                                lockDuration: TimeSpan.FromMinutes(1),
                                requiresSession: false,
                                defaultMessageTimeToLive: TimeSpan.MaxValue,
                                autoDeleteOnIdle: TimeSpan.MaxValue,
                                deadLetteringOnMessageExpiration: false,
                                maxDeliveryCount: 10,
                                enableBatchedOperations: true,
                                status: EntityStatus.Active,
                                forwardTo: null,
                                forwardDeadLetteredMessagesTo: null,
                                userMetadata: string.Empty
                            )
                        )
                );
            });
        Administration
            .DeleteSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = Key(call.ArgAt<string>(0), call.ArgAt<string>(1));
                Writes.Add($"delete subscription {key}");
                return Subscriptions.Remove(key)
                    ? Task.FromResult(Substitute.For<Response>())
                    : Task.FromException<Response>(_NotFound(key));
            });
        Administration
            .GetSubscriptionRuntimePropertiesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = Key(call.ArgAt<string>(0), call.ArgAt<string>(1));
                return Subscriptions.ContainsKey(key)
                    ? Task.FromResult(
                        _Response(
                            ServiceBusModelFactory.SubscriptionRuntimeProperties(
                                call.ArgAt<string>(0),
                                call.ArgAt<string>(1),
                                activeMessageCount: Messages.GetValueOrDefault(key),
                                deadLetterMessageCount: Messages.GetValueOrDefault(DeadLetter(key)),
                                transferDeadLetterMessageCount: 0,
                                transferMessageCount: 0,
                                totalMessageCount: 0,
                                createdAt: default,
                                updatedAt: default,
                                accessedAt: default
                            )
                        )
                    )
                    : Task.FromException<Response<SubscriptionRuntimeProperties>>(_NotFound(key));
            });
    }

    private void _CreateSubscription(CreateSubscriptionOptions options)
    {
        var key = Key(options.TopicName, options.SubscriptionName);
        Writes.Add($"create subscription {key}");

        if (!Topics.Contains(options.TopicName))
        {
            throw _NotFound(options.TopicName);
        }

        Subscriptions[key] = new Dictionary<string, RuleFilter>(StringComparer.Ordinal);
    }

    private void _StubRules()
    {
        Administration
            .GetRulesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = Key(call.ArgAt<string>(0), call.ArgAt<string>(1));

                if (!Subscriptions.TryGetValue(key, out var rules))
                {
                    throw _NotFound(key);
                }

                return _Pageable(
                    rules.Select(static rule => ServiceBusModelFactory.RuleProperties(rule.Key, rule.Value))
                );
            });
        Administration
            .CreateRuleAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CreateRuleOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var key = Key(call.ArgAt<string>(0), call.ArgAt<string>(1));
                var rule = call.Arg<CreateRuleOptions>();
                Writes.Add($"create rule {key}/{rule.Name}");
                Subscriptions[key][rule.Name] = rule.Filter;
                return Task.FromResult<Response<RuleProperties>>(null!);
            });
        Administration
            .DeleteRuleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = Key(call.ArgAt<string>(0), call.ArgAt<string>(1));
                Writes.Add($"delete rule {key}/{call.ArgAt<string>(2)}");
                Subscriptions[key].Remove(call.ArgAt<string>(2));
                return Task.FromResult(Substitute.For<Response>());
            });
        Administration
            .UpdateRuleAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<RuleProperties>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var key = Key(call.ArgAt<string>(0), call.ArgAt<string>(1));
                var rule = call.Arg<RuleProperties>();
                Writes.Add($"update rule {key}/{rule.Name}");
                Subscriptions[key][rule.Name] = rule.Filter;
                return Task.FromResult<Response<RuleProperties>>(null!);
            });
    }

    private void _StubQueues()
    {
        Administration
            .QueueExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_Response(Queues.Contains(call.ArgAt<string>(0)))));
        Administration
            .CreateQueueAsync(Arg.Any<CreateQueueOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var name = call.Arg<CreateQueueOptions>().Name;
                Writes.Add($"create queue {name}");
                Queues.Add(name);
                return Task.FromResult<Response<QueueProperties>>(null!);
            });
        Administration
            .GetQueuesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
                _Pageable(
                    Queues.Select(static name =>
                        ServiceBusModelFactory.QueueProperties(
                            name,
                            lockDuration: TimeSpan.FromMinutes(1),
                            maxSizeInMegabytes: 1024,
                            requiresDuplicateDetection: false,
                            requiresSession: false,
                            defaultMessageTimeToLive: TimeSpan.MaxValue,
                            autoDeleteOnIdle: TimeSpan.MaxValue,
                            deadLetteringOnMessageExpiration: false,
                            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(1),
                            maxDeliveryCount: 10,
                            enableBatchedOperations: true,
                            status: EntityStatus.Active,
                            forwardTo: null,
                            forwardDeadLetteredMessagesTo: null,
                            userMetadata: string.Empty,
                            enablePartitioning: false
                        )
                    )
                )
            );
        Administration
            .DeleteQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                Writes.Add($"delete queue {name}");
                return Queues.Remove(name)
                    ? Task.FromResult(Substitute.For<Response>())
                    : Task.FromException<Response>(_NotFound(name));
            });
        Administration
            .GetQueueRuntimePropertiesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                return Queues.Contains(name)
                    ? Task.FromResult(
                        _Response(
                            ServiceBusModelFactory.QueueRuntimeProperties(
                                name,
                                activeMessageCount: Messages.GetValueOrDefault(name),
                                scheduledMessageCount: 0,
                                deadLetterMessageCount: Messages.GetValueOrDefault(DeadLetter(name)),
                                transferDeadLetterMessageCount: 0,
                                transferMessageCount: 0,
                                totalMessageCount: 0,
                                sizeInBytes: 0,
                                createdAt: default,
                                updatedAt: default,
                                accessedAt: default
                            )
                        )
                    )
                    : Task.FromException<Response<QueueRuntimeProperties>>(_NotFound(name));
            });
    }

    // A receive-and-delete receiver takes up to the batch size of the entity's waiting messages, and an empty batch once
    // none is left; one on an entity that does not exist fails as Service Bus does.
    private void _StubReceivers()
    {
        Client
            .CreateReceiver(Arg.Any<string>(), Arg.Any<ServiceBusReceiverOptions>())
            .Returns(call => _Receiver(call.ArgAt<string>(0), call.Arg<ServiceBusReceiverOptions>()));
        Client
            .CreateReceiver(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ServiceBusReceiverOptions>())
            .Returns(call =>
                _Receiver(Key(call.ArgAt<string>(0), call.ArgAt<string>(1)), call.Arg<ServiceBusReceiverOptions>())
            );
    }

    private ServiceBusReceiver _Receiver(string entity, ServiceBusReceiverOptions options)
    {
        var path = options.SubQueue is SubQueue.DeadLetter ? DeadLetter(entity) : entity;
        var receiver = Substitute.For<ServiceBusReceiver>();
        receiver
            .ReceiveMessagesAsync(Arg.Any<int>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (options.ReceiveMode is not ServiceBusReceiveMode.ReceiveAndDelete)
                {
                    throw new InvalidOperationException("Only a receive-and-delete receiver removes messages.");
                }

                if (!Queues.Contains(entity) && !Subscriptions.ContainsKey(entity))
                {
                    throw _NotFound(entity);
                }

                var taken = Math.Min(call.ArgAt<int>(0), Messages.GetValueOrDefault(path));
                Messages[path] = Messages.GetValueOrDefault(path) - taken;
                IReadOnlyList<ServiceBusReceivedMessage> batch =
                [
                    .. Enumerable
                        .Range(0, taken)
                        .Select(static _ => ServiceBusModelFactory.ServiceBusReceivedMessage()),
                ];
                return Task.FromResult(batch);
            });

        return receiver;
    }
}
