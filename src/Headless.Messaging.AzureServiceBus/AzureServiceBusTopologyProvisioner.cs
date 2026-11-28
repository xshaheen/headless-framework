// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Hosting;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>
/// Provisions the shared Azure Service Bus topology: the Bus topic and every custom producer's topic, one subscription
/// per competing Bus consumer identity with one rule per message it consumes, and one queue per Queue-lane message this
/// host consumes or publishes.
/// </summary>
/// <remarks>
/// Per-process objects stay the running host's: an every-instance consumer's subscription is named after the process and
/// deleted when it stops, and a subscription an <c>IRuntimeSubscriber</c> adds is not in the topology at all.
/// </remarks>
internal sealed class AzureServiceBusTopologyProvisioner(
    IAzureServiceBusClientPool pool,
    IOptions<AzureServiceBusMessagingOptions> options,
    ILogger<AzureServiceBusTopologyProvisioner> logger
) : ITransportTopologyProvisioner
{
    private readonly AzureServiceBusMessagingOptions _options = options.Value;

    public string Kind => SetupMessagingAzureServiceBus.TopologyResourceKind;

    public async Task ProvisionAsync(MessagingTopology topology, CancellationToken cancellationToken)
    {
        var plan = Plan(topology, _options);
        var administration = pool.GetAdministrationClient();

        foreach (var (topicPath, _) in plan.Topics)
        {
            await AzureServiceBusTopology
                .EnsureTopicAsync(administration, topicPath, logger, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var subscription in plan.Subscriptions)
        {
            await AzureServiceBusTopology
                .EnsureSubscriptionAsync(
                    administration,
                    _options,
                    AzureServiceBusTopology.SubscriptionOptions(
                        _options,
                        subscription.TopicPath,
                        subscription.Name,
                        _options.SubscriptionAutoDeleteOnIdle
                    ),
                    subscription.RuleNames?[0],
                    logger,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (subscription.RuleNames is { } ruleNames)
            {
                await AzureServiceBusTopology
                    .SyncRulesAsync(
                        administration,
                        _options,
                        subscription.TopicPath,
                        subscription.Name,
                        ruleNames,
                        logger,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
        }

        foreach (var queue in plan.Queues)
        {
            await AzureServiceBusTopology
                .EnsureQueueAsync(
                    administration,
                    AzureServiceBusTopology.QueueOptions(_options, queue.Name),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    // One listing of the namespace's topics and, when this host needs a queue, one of its queues (each a call per 100
    // entities), one subscription listing per custom producer topic this host subscribes on, and one rule read per
    // shared subscription on the Bus topic. The rule read also proves the subscription exists, and it is what catches a
    // consumer that gained or lost a message since the last provision, which would otherwise drop or dead-letter
    // messages without any failed receive.
    public async Task VerifyAsync(MessagingTopology topology, CancellationToken cancellationToken)
    {
        var plan = Plan(topology, _options);
        var administration = pool.GetAdministrationClient();
        var problems = new List<string>();

        var topics = await _ListAsync(administration.GetTopicsAsync(cancellationToken), static t => t.Name)
            .ConfigureAwait(false);

        foreach (var (topicPath, _) in plan.Topics.Where(t => !topics.Contains(t.TopicPath)))
        {
            problems.Add($"topic '{topicPath}' does not exist");
        }

        foreach (var group in plan.Subscriptions.GroupBy(static s => s.TopicPath, StringComparer.OrdinalIgnoreCase))
        {
            // Its subscriptions are already reported with it.
            if (!topics.Contains(group.Key))
            {
                continue;
            }

            HashSet<string>? listed = null;

            foreach (var subscription in group)
            {
                if (subscription.RuleNames is not { } ruleNames)
                {
                    listed ??= await _ListAsync(
                            administration.GetSubscriptionsAsync(group.Key, cancellationToken),
                            static s => s.SubscriptionName
                        )
                        .ConfigureAwait(false);

                    if (!listed.Contains(subscription.Name))
                    {
                        problems.Add(_Missing(subscription));
                    }

                    continue;
                }

                await _VerifyRulesAsync(administration, subscription, ruleNames, problems, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (plan.Queues.Count > 0)
        {
            var queues = await _ListAsync(administration.GetQueuesAsync(cancellationToken), static q => q.Name)
                .ConfigureAwait(false);

            foreach (var queue in plan.Queues.Where(q => !queues.Contains(q.Name)))
            {
                problems.Add($"queue '{queue.Name}' does not exist");
            }
        }

        if (problems.Count > 0)
        {
            throw NotProvisioned(problems);
        }
    }

    // Service Bus has no purge call, so this receives and deletes every message of each owned entity and of its
    // dead-letter subqueue until a receive comes back empty. A message another receiver holds locked, or one scheduled
    // for later, is not yet available, so it stays.
    public async Task PurgeAsync(MessagingTopology topology, CancellationToken cancellationToken)
    {
        var plan = Plan(topology, _options);
        var client = pool.GetClient();
        var problems = new List<string>();

        foreach (var (entity, path, subscription) in _OwnedEntities(plan))
        {
            try
            {
                // A session entity hands out its messages one session at a time; its dead-letter subqueue never does.
                if (_options.EnableSessions)
                {
                    await _DrainSessionsAsync(client, path, subscription, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _DrainAsync(client, path, subscription, SubQueue.None, cancellationToken)
                        .ConfigureAwait(false);
                }

                await _DrainAsync(client, path, subscription, SubQueue.DeadLetter, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceBusException e) when (AzureServiceBusTopology.IsNotFound(e))
            {
                problems.Add($"{entity} does not exist");
            }
        }

        if (problems.Count > 0)
        {
            throw NotProvisioned(problems);
        }
    }

    // Deletes only the subscriptions and queues this host consumes; deleting one that does not exist succeeds, so a half
    // torn-down topology finishes cleanly. The topics stay: TopicPath is shared by every service on the namespace by
    // default, deleting a topic deletes every other service's subscriptions on it, and a publisher's sends would fail.
    public async Task DeleteAsync(MessagingTopology topology, CancellationToken cancellationToken)
    {
        var plan = Plan(topology, _options);
        var administration = pool.GetAdministrationClient();

        foreach (var subscription in plan.Subscriptions)
        {
            await _IgnoreMissingAsync(() =>
                    administration.DeleteSubscriptionAsync(subscription.TopicPath, subscription.Name, cancellationToken)
                )
                .ConfigureAwait(false);
        }

        foreach (var queue in plan.Queues.Where(static q => q.Owned))
        {
            await _IgnoreMissingAsync(() => administration.DeleteQueueAsync(queue.Name, cancellationToken))
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
        MessagingTopology topology,
        CancellationToken cancellationToken
    )
    {
        var plan = Plan(topology, _options);
        var administration = pool.GetAdministrationClient();
        var status = new List<KeyValuePair<string, string>>();

        foreach (var (topicPath, _) in plan.Topics)
        {
            var exists = await administration.TopicExistsAsync(topicPath, cancellationToken).ConfigureAwait(false);
            status.Add(new($"topic {topicPath}", exists.Value ? "exists" : "missing"));
        }

        foreach (var subscription in plan.Subscriptions)
        {
            var value = await _CountsOrMissingAsync(async () =>
                {
                    var properties = await administration
                        .GetSubscriptionRuntimePropertiesAsync(
                            subscription.TopicPath,
                            subscription.Name,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    return (properties.Value.ActiveMessageCount, properties.Value.DeadLetterMessageCount);
                })
                .ConfigureAwait(false);
            status.Add(new($"subscription {subscription.TopicPath}/{subscription.Name}", value));
        }

        foreach (var queue in plan.Queues)
        {
            var value = await _CountsOrMissingAsync(async () =>
                {
                    var properties = await administration
                        .GetQueueRuntimePropertiesAsync(queue.Name, cancellationToken)
                        .ConfigureAwait(false);

                    return (properties.Value.ActiveMessageCount, properties.Value.DeadLetterMessageCount);
                })
                .ConfigureAwait(false);
            status.Add(new($"queue {queue.Name}", value));
        }

        return status;
    }

    /// <summary>
    /// The topics every host of these options publishes or consumes through, in a stable order: the Bus topic and each
    /// custom producer's topic, with whether a competing consumer identity also gets a subscription on it.
    /// </summary>
    internal static List<(string TopicPath, bool Subscribe)> Topics(AzureServiceBusMessagingOptions options) =>
        [
            .. options
                .CustomProducers.Select(static producer => (producer.TopicPath, Subscribe: producer.CreateSubscription))
                .Append((options.TopicPath, Subscribe: true))
                .GroupBy(static n => n.TopicPath, StringComparer.OrdinalIgnoreCase)
                .Select(static n => (TopicPath: n.Key, Subscribe: n.Any(static o => o.Subscribe)))
                .OrderBy(static n => n.TopicPath, StringComparer.Ordinal),
        ];

    /// <summary>The shared entities <paramref name="topology"/> needs under <paramref name="options"/>.</summary>
    /// <exception cref="InvalidOperationException">A derived subscription or queue name breaks Service Bus naming rules.</exception>
    internal static AzureServiceBusTopologyPlan Plan(
        MessagingTopology topology,
        AzureServiceBusMessagingOptions options
    )
    {
        var topics = Topics(options);
        var subscriptions = new List<AzureServiceBusTopologySubscription>();
        var queues = new SortedDictionary<string, AzureServiceBusTopologyQueue>(StringComparer.Ordinal);

        foreach (var subscription in topology.Subscriptions)
        {
            if (subscription.Kind is ConsumerSubscriptionKind.EveryInstance)
            {
                continue;
            }

            if (subscription.Lane is MessageLane.Queue)
            {
                foreach (var messageName in subscription.MessageNames)
                {
                    _AddQueue(queues, options, messageName, owned: true);
                }

                continue;
            }

            var name = AzureServiceBusConsumerClientFactory.BusSubscriptionName(subscription.SubscriptionName);
            AzureServiceBusConsumerClient.CheckValidSubscriptionName(name);

            foreach (var (topicPath, subscribe) in topics)
            {
                if (!subscribe)
                {
                    continue;
                }

                // Only the Bus topic is read, so only its subscription filters to the consumer's messages; one on a
                // custom producer's topic keeps whatever rules it holds.
                var isBusTopic = string.Equals(topicPath, options.TopicPath, StringComparison.OrdinalIgnoreCase);
                subscriptions.Add(
                    new AzureServiceBusTopologySubscription(
                        topicPath,
                        name,
                        isBusTopic ? AzureServiceBusTopology.RuleNames(options, subscription.MessageNames) : null
                    )
                );
            }
        }

        foreach (var message in topology.Messages.Where(static m => m.Lane is MessageLane.Queue))
        {
            _AddQueue(queues, options, message.MessageName, owned: false);
        }

        return new AzureServiceBusTopologyPlan(
            topics,
            [
                .. subscriptions
                    .DistinctBy(static s => (s.TopicPath, s.Name))
                    .OrderBy(static s => s.TopicPath, StringComparer.Ordinal)
                    .ThenBy(static s => s.Name, StringComparer.Ordinal),
            ],
            [.. queues.Values]
        );
    }

    /// <summary>The error a check raises for a topology the deployment has not set up, naming each gap.</summary>
    internal static ResourceProvisioningException NotProvisioned(IReadOnlyList<string> problems) =>
        new(
            $"Azure Service Bus topology is not provisioned: {string.Join("; ", problems)}. "
                + AzureServiceBusTopology.ProvisionHint
        );

    private static void _AddQueue(
        SortedDictionary<string, AzureServiceBusTopologyQueue> queues,
        AzureServiceBusMessagingOptions options,
        string messageName,
        bool owned
    )
    {
        var name = AzureServiceBusPhysicalAddress.Queue(options.HostPrefix, messageName);

        if (queues.TryGetValue(name, out var queue))
        {
            queue.Owned |= owned;

            return;
        }

        AzureServiceBusConsumerClient.CheckValidQueueName(name);
        queues[name] = new AzureServiceBusTopologyQueue(name) { Owned = owned };
    }

    private static async Task _VerifyRulesAsync(
        ServiceBusAdministrationClient administration,
        AzureServiceBusTopologySubscription subscription,
        IReadOnlyList<string> ruleNames,
        List<string> problems,
        CancellationToken cancellationToken
    )
    {
        List<RuleProperties> rules;

        try
        {
            rules = await AzureServiceBusTopology
                .GetRulesAsync(administration, subscription.TopicPath, subscription.Name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ServiceBusException e) when (AzureServiceBusTopology.IsNotFound(e))
        {
            problems.Add(_Missing(subscription));

            return;
        }

        var held = rules.ConvertAll(static rule => rule.Name);
        var lacking = ruleNames.Except(held, StringComparer.Ordinal).ToList();
        var extra = held.Except(ruleNames, StringComparer.Ordinal).ToList();

        if (lacking.Count > 0)
        {
            problems.Add($"{_Describe(subscription)} lacks the rules {_Quote(lacking)}");
        }

        if (extra.Count > 0)
        {
            problems.Add($"{_Describe(subscription)} holds the rules {_Quote(extra)} no registered consumer needs");
        }
    }

    private static IEnumerable<(string Entity, string Path, string? Subscription)> _OwnedEntities(
        AzureServiceBusTopologyPlan plan
    )
    {
        foreach (var subscription in plan.Subscriptions)
        {
            yield return (_Describe(subscription), subscription.TopicPath, subscription.Name);
        }

        foreach (var queue in plan.Queues.Where(static q => q.Owned))
        {
            yield return ($"queue '{queue.Name}'", queue.Name, null);
        }
    }

    private static string _Describe(AzureServiceBusTopologySubscription subscription) =>
        $"subscription '{subscription.Name}' on topic '{subscription.TopicPath}'";

    private static string _Missing(AzureServiceBusTopologySubscription subscription) =>
        $"{_Describe(subscription)} does not exist";

    private static string _Quote(IEnumerable<string> names) => string.Join(", ", names.Select(static n => $"'{n}'"));

    private static async Task<HashSet<string>> _ListAsync<T>(Azure.AsyncPageable<T> pageable, Func<T, string> name)
        where T : notnull
    {
        // Entity names are case-insensitive in Service Bus.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

#pragma warning disable MA0080 // False positive: the pageable already carries the cancellation token.
        await foreach (var item in pageable.ConfigureAwait(false))
#pragma warning restore MA0080
        {
            names.Add(name(item));
        }

        return names;
    }

    // Each empty receive waits this long for a message before the entity counts as drained.
    private static readonly TimeSpan _DrainWait = TimeSpan.FromSeconds(1);
    private const int _DrainBatch = 250;

    private static async Task _DrainAsync(
        ServiceBusClient client,
        string path,
        string? subscription,
        SubQueue subQueue,
        CancellationToken cancellationToken
    )
    {
        var options = new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
            SubQueue = subQueue,
        };
        var receiver = subscription is null
            ? client.CreateReceiver(path, options)
            : client.CreateReceiver(path, subscription, options);

        await using (receiver.ConfigureAwait(false))
        {
            await _DrainReceiverAsync(receiver, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task _DrainSessionsAsync(
        ServiceBusClient client,
        string path,
        string? subscription,
        CancellationToken cancellationToken
    )
    {
        var options = new ServiceBusSessionReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete };

        while (true)
        {
            ServiceBusSessionReceiver receiver;

            try
            {
                receiver = subscription is null
                    ? await client.AcceptNextSessionAsync(path, options, cancellationToken).ConfigureAwait(false)
                    : await client
                        .AcceptNextSessionAsync(path, subscription, options, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (ServiceBusException e) when (e.Reason is ServiceBusFailureReason.ServiceTimeout)
            {
                // No session holds a message.
                return;
            }

            await using (receiver.ConfigureAwait(false))
            {
                await _DrainReceiverAsync(receiver, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task _DrainReceiverAsync(ServiceBusReceiver receiver, CancellationToken cancellationToken)
    {
        while (true)
        {
            var batch = await receiver
                .ReceiveMessagesAsync(_DrainBatch, _DrainWait, cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                return;
            }
        }
    }

    private static async Task<string> _CountsOrMissingAsync(Func<Task<(long Active, long DeadLettered)>> read)
    {
        try
        {
            var (active, deadLettered) = await read().ConfigureAwait(false);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{active} active, {deadLettered} dead-lettered messages"
            );
        }
        catch (ServiceBusException e) when (AzureServiceBusTopology.IsNotFound(e))
        {
            return "missing";
        }
    }

    private static async Task _IgnoreMissingAsync(Func<Task<Azure.Response>> delete)
    {
        try
        {
            await delete().ConfigureAwait(false);
        }
        catch (ServiceBusException e) when (AzureServiceBusTopology.IsNotFound(e))
        {
            // Already gone.
        }
    }
}

/// <summary>The shared entities one host's topology needs, in a stable order.</summary>
internal sealed record AzureServiceBusTopologyPlan(
    IReadOnlyList<(string TopicPath, bool Subscribe)> Topics,
    IReadOnlyList<AzureServiceBusTopologySubscription> Subscriptions,
    IReadOnlyList<AzureServiceBusTopologyQueue> Queues
);

/// <summary>
/// One shared subscription of a competing Bus consumer identity. <see cref="RuleNames"/> is the rule set provision keeps
/// it at, or <see langword="null"/> on a custom producer's topic, where the subscription keeps whatever rules it holds.
/// </summary>
internal sealed record AzureServiceBusTopologySubscription(
    string TopicPath,
    string Name,
    IReadOnlyList<string>? RuleNames
);

/// <summary>One Queue-lane queue the topology needs.</summary>
internal sealed record AzureServiceBusTopologyQueue(string Name)
{
    /// <summary>
    /// Whether this host consumes the queue. Purge and delete act only on owned queues; a queue this host only publishes
    /// to is created and checked, never purged or deleted.
    /// </summary>
    public bool Owned { get; set; }
}
