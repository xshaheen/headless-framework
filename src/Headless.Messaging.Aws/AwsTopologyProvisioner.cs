// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Hosting;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Aws;

/// <summary>
/// Provisions the shared SNS/SQS topology: one SNS topic per Bus message this host consumes or registers, one SQS queue
/// per competing Bus consumer identity with the access policy and the raw-delivery SNS subscriptions that feed it, and
/// one SQS queue per Queue-lane message this host consumes or registers.
/// </summary>
/// <remarks>
/// Every-instance subscriptions are skipped: the transport refuses them at startup, since SQS has no idle deletion. A
/// subscription an <c>IRuntimeSubscriber</c> adds is not in the topology at all, and its client creates what it needs.
/// </remarks>
internal sealed class AwsTopologyProvisioner : ITransportTopologyProvisioner
{
    private readonly AmazonSqsMessagingOptions _options;
    private readonly IAmazonSQS? _sqs;
    private readonly IAmazonSimpleNotificationService? _sns;

    public AwsTopologyProvisioner(IOptions<AmazonSqsMessagingOptions> options)
        : this(options.Value, sqs: null, sns: null) { }

    private AwsTopologyProvisioner(
        AmazonSqsMessagingOptions options,
        IAmazonSQS? sqs,
        IAmazonSimpleNotificationService? sns
    )
    {
        _options = options;
        _sqs = sqs;
        _sns = sns;
    }

    /// <summary>A provisioner over the given clients, which it never disposes; for tests.</summary>
    internal static AwsTopologyProvisioner Create(
        AmazonSqsMessagingOptions options,
        IAmazonSQS sqs,
        IAmazonSimpleNotificationService sns
    ) => new(options, sqs, sns);

    public string Kind => SetupMessagingAws.TopologyResourceKind;

    public Task ProvisionAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
        _RunAsync(
            async (sqs, sns) =>
            {
                var plan = Plan(topology, _options.HostPrefix);
                var topicArns = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var topic in plan.Topics)
                {
                    topicArns[topic] = await AwsTopology
                        .CreateTopicAsync(sns, topic, cancellationToken)
                        .ConfigureAwait(false);
                }

                foreach (var queue in plan.Queues)
                {
                    var queueUrl = await AwsTopology
                        .CreateQueueAsync(sqs, queue.Name, cancellationToken)
                        .ConfigureAwait(false);

                    if (queue.Topics.Count == 0)
                    {
                        continue;
                    }

                    var arns = queue.Topics.Select(topic => topicArns[topic]).ToList();

                    // The policy first, so SNS can deliver the moment the subscription exists.
                    var queueArn = await AwsTopology
                        .AllowTopicsAsync(sqs, queueUrl, arns, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var topicArn in arns)
                    {
                        await AwsTopology
                            .SubscribeQueueAsync(sns, topicArn, queueArn, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            },
            cancellationToken
        );

    // Reads only, in as few calls as SNS and SQS allow: SNS has no topic lookup by name, so one ListTopics per 100
    // topics in the account; one GetQueueUrl per queue; for a Bus identity queue one GetQueueAttributes (ARN and
    // policy), one ListSubscriptionsByTopic per 100 subscriptions of each of its topics, and one
    // GetSubscriptionAttributes per subscription for raw delivery. Every missing object is named in one failure.
    public Task VerifyAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
        _RunAsync(
            async (sqs, sns) =>
            {
                var plan = Plan(topology, _options.HostPrefix);
                var missing = new List<string>();
                var topicArns =
                    plan.Topics.Count == 0
                        ? []
                        : await AwsTopology.ListTopicArnsAsync(sns, cancellationToken).ConfigureAwait(false);

                missing.AddRange(plan.Topics.Where(t => !topicArns.ContainsKey(t)).Select(t => $"SNS topic '{t}'"));

                foreach (var queue in plan.Queues)
                {
                    var queueUrl = await AwsTopology
                        .TryGetQueueUrlAsync(sqs, queue.Name, cancellationToken)
                        .ConfigureAwait(false);

                    if (queueUrl is null)
                    {
                        missing.Add($"SQS queue '{queue.Name}'");
                        continue;
                    }

                    if (queue.Topics.Count == 0)
                    {
                        continue;
                    }

                    var (queueArn, policy) = await AwsTopology
                        .GetQueueArnAndPolicyAsync(sqs, queueUrl, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var topic in queue.Topics)
                    {
                        if (!topicArns.TryGetValue(topic, out var topicArn))
                        {
                            continue; // Already reported as a missing topic.
                        }

                        if (!policy.HasSqsPermission(topicArn, queueArn))
                        {
                            missing.Add($"SQS queue policy of '{queue.Name}' allowing SNS topic '{topic}'");
                        }

                        var subscriptions = await AwsTopology
                            .ListQueueSubscriptionsAsync(sns, topicArn, cancellationToken)
                            .ConfigureAwait(false);
                        var raw = false;

                        foreach (var subscriptionArn in subscriptions[queueArn])
                        {
                            if (
                                await AwsTopology
                                    .IsRawDeliveryAsync(sns, subscriptionArn, cancellationToken)
                                    .ConfigureAwait(false)
                            )
                            {
                                raw = true;
                                break;
                            }
                        }

                        if (!raw)
                        {
                            missing.Add(
                                $"SNS subscription of SQS queue '{queue.Name}' to topic '{topic}' with raw message delivery"
                            );
                        }
                    }
                }

                if (missing.Count > 0)
                {
                    throw AwsTopology.Missing(missing);
                }
            },
            cancellationToken
        );

    // SQS allows one purge per queue every 60 seconds; a purge already in progress empties the queue all the same.
    public Task PurgeAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
        _RunAsync(
            async (sqs, _) =>
            {
                foreach (var queue in Plan(topology, _options.HostPrefix).Queues.Where(static q => q.Owned))
                {
                    var queueUrl = await AwsTopology
                        .RequireQueueUrlAsync(sqs, queue.Name, cancellationToken)
                        .ConfigureAwait(false);

                    try
                    {
                        await sqs.PurgeQueueAsync(queueUrl, cancellationToken).ConfigureAwait(false);
                    }
                    catch (PurgeQueueInProgressException)
                    {
                        // Another purge of this queue started less than 60 seconds ago and removes the same messages.
                    }
                }
            },
            cancellationToken
        );

    // Deletes only the queues this host consumes, and the SNS subscriptions that feed them, so no topic keeps delivering
    // to a deleted queue. A queue that does not exist is skipped, so a half torn-down topology finishes cleanly. The
    // topics stay: every service that publishes or consumes a message shares its topic, and a publish-only service
    // leaves no subscription that could prove it still uses one.
    public Task DeleteAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
        _RunAsync(
            async (sqs, sns) =>
            {
                var plan = Plan(topology, _options.HostPrefix);
                var owned = plan.Queues.Where(static q => q.Owned).ToList();
                var topicArns = owned.Exists(static q => q.Topics.Count > 0)
                    ? await AwsTopology.ListTopicArnsAsync(sns, cancellationToken).ConfigureAwait(false)
                    : [];

                foreach (var queue in owned)
                {
                    var queueUrl = await AwsTopology
                        .TryGetQueueUrlAsync(sqs, queue.Name, cancellationToken)
                        .ConfigureAwait(false);

                    if (queueUrl is null)
                    {
                        continue;
                    }

                    if (queue.Topics.Count > 0)
                    {
                        var (queueArn, _) = await AwsTopology
                            .GetQueueArnAndPolicyAsync(sqs, queueUrl, cancellationToken)
                            .ConfigureAwait(false);

                        foreach (var topic in queue.Topics)
                        {
                            if (!topicArns.TryGetValue(topic, out var topicArn))
                            {
                                continue;
                            }

                            var subscriptions = await AwsTopology
                                .ListQueueSubscriptionsAsync(sns, topicArn, cancellationToken)
                                .ConfigureAwait(false);

                            foreach (var subscriptionArn in subscriptions[queueArn])
                            {
                                await sns.UnsubscribeAsync(subscriptionArn, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }

                    try
                    {
                        await sqs.DeleteQueueAsync(queueUrl, cancellationToken).ConfigureAwait(false);
                    }
                    catch (QueueDoesNotExistException)
                    {
                        // Deleted between the lookup and the delete: what this step was for.
                    }
                }
            },
            cancellationToken
        );

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
        MessagingTopology topology,
        CancellationToken cancellationToken
    )
    {
        var status = new List<KeyValuePair<string, string>>();

        await _RunAsync(
                async (sqs, sns) =>
                {
                    var plan = Plan(topology, _options.HostPrefix);
                    var topicArns =
                        plan.Topics.Count == 0
                            ? []
                            : await AwsTopology.ListTopicArnsAsync(sns, cancellationToken).ConfigureAwait(false);

                    foreach (var topic in plan.Topics)
                    {
                        status.Add(new(topic, topicArns.ContainsKey(topic) ? "topic" : "topic missing"));
                    }

                    foreach (var queue in plan.Queues)
                    {
                        var queueUrl = await AwsTopology
                            .TryGetQueueUrlAsync(sqs, queue.Name, cancellationToken)
                            .ConfigureAwait(false);

                        if (queueUrl is null)
                        {
                            status.Add(new(queue.Name, "queue missing"));
                            continue;
                        }

                        var attributes = await sqs.GetQueueAttributesAsync(
                                new GetQueueAttributesRequest
                                {
                                    QueueUrl = queueUrl,
                                    AttributeNames =
                                    [
                                        "ApproximateNumberOfMessages",
                                        "ApproximateNumberOfMessagesNotVisible",
                                    ],
                                },
                                cancellationToken
                            )
                            .ConfigureAwait(false);

                        status.Add(
                            new(
                                queue.Name,
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"{attributes.ApproximateNumberOfMessages} messages, {attributes.ApproximateNumberOfMessagesNotVisible} in flight"
                                )
                            )
                        );
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return status;
    }

    /// <summary>
    /// The shared objects the topology needs, in a stable order: the topics of every Bus message consumed or registered,
    /// a queue per competing Bus identity (subscribed to the topics of the messages it consumes), and a queue per
    /// Queue-lane message consumed or registered. A queue is <see cref="AwsTopologyQueue.Owned"/> only when this host
    /// consumes it; a queue it only publishes to belongs to the consuming service.
    /// </summary>
    internal static AwsTopologyPlan Plan(MessagingTopology topology, string hostPrefix = "")
    {
        var topics = new SortedSet<string>(StringComparer.Ordinal);
        var queues = new SortedDictionary<string, AwsTopologyQueue>(StringComparer.Ordinal);

        foreach (var subscription in topology.Subscriptions)
        {
            if (subscription.Kind is ConsumerSubscriptionKind.EveryInstance)
            {
                continue;
            }

            if (subscription.Lane is MessageLane.Bus)
            {
                var queue = _Add(
                    queues,
                    AwsPhysicalAddress.BusSubscriptionQueue(hostPrefix, subscription.SubscriptionName),
                    owned: true
                );

                foreach (var messageName in subscription.MessageNames)
                {
                    var topic = AwsPhysicalAddress.BusTopic(hostPrefix, messageName);
                    topics.Add(topic);
                    queue.Topics.Add(topic);
                }

                continue;
            }

            foreach (var messageName in subscription.MessageNames)
            {
                _Add(queues, AwsPhysicalAddress.QueueDestination(hostPrefix, messageName), owned: true);
            }
        }

        foreach (var message in topology.Messages)
        {
            if (message.Lane is MessageLane.Bus)
            {
                topics.Add(AwsPhysicalAddress.BusTopic(hostPrefix, message.MessageName));
            }
            else
            {
                _Add(queues, AwsPhysicalAddress.QueueDestination(hostPrefix, message.MessageName), owned: false);
            }
        }

        return new AwsTopologyPlan([.. topics], [.. queues.Values]);
    }

    private static AwsTopologyQueue _Add(SortedDictionary<string, AwsTopologyQueue> queues, string name, bool owned)
    {
        if (!queues.TryGetValue(name, out var queue))
        {
            queue = new AwsTopologyQueue(name, new SortedSet<string>(StringComparer.Ordinal));
            queues[name] = queue;
        }

        queue.Owned |= owned;

        return queue;
    }

    // Clients of their own, disposed after the operation: a deploy step runs once, and the transports' clients belong
    // to the running host.
    private async Task _RunAsync(
        Func<IAmazonSQS, IAmazonSimpleNotificationService, Task> action,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_sqs is not null && _sns is not null)
        {
            await action(_sqs, _sns).ConfigureAwait(false);
            return;
        }

        using var sqs = AwsClientFactory.CreateSqsClient(_options);
        using var sns = AwsClientFactory.CreateSnsClient(_options);

        await action(sqs, sns).ConfigureAwait(false);
    }
}

/// <summary>The topics and queues one topology needs.</summary>
internal sealed record AwsTopologyPlan(IReadOnlyList<string> Topics, IReadOnlyList<AwsTopologyQueue> Queues);

/// <summary>One shared SQS queue, with the SNS topics subscribed to it (none on the Queue lane).</summary>
internal sealed record AwsTopologyQueue(string Name, SortedSet<string> Topics)
{
    /// <summary>
    /// Whether this host consumes the queue. Purge and delete act only on owned queues; a queue this host only
    /// publishes to is provisioned and verified, never purged or deleted.
    /// </summary>
    public bool Owned { get; set; }
}
