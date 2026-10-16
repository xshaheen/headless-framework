// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// Provisions the shared RabbitMQ topology: the two lane exchanges, and one queue, with its dead-letter queue and its
/// bindings, per competing consumer subscription and per registered Queue-lane message.
/// </summary>
/// <remarks>
/// Per-process objects stay the running host's: an every-instance consumer's exclusive queue and the request/reply
/// queue are named by the broker or the process, and a subscription an <c>IRuntimeSubscriber</c> adds is not in the
/// topology at all.
/// </remarks>
internal sealed class RabbitMqTopologyProvisioner(
    IConnectionChannelPool pool,
    IOptions<RabbitMqMessagingOptions> options
) : ITransportTopologyProvisioner
{
    private static readonly MessageLane[] _Lanes = [MessageLane.Bus, MessageLane.Queue];

    private readonly RabbitMqMessagingOptions _options = options.Value;

    public string Broker => "RabbitMQ";

    public Task SetupAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
        _RunAsync(
            async channel =>
            {
                foreach (var lane in _Lanes)
                {
                    await RabbitMqQueueTopology
                        .EnsureExchangeAsync(
                            channel,
                            _Exchange(lane),
                            RabbitMqPhysicalAddress.ExchangeType(lane),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }

                foreach (var queue in Queues(topology))
                {
                    var laneExchange = _Exchange(queue.Lane);

                    await RabbitMqQueueTopology
                        .EnsureQueueAsync(
                            channel,
                            _options,
                            laneExchange,
                            queue.Name,
                            routingKey: null,
                            cancellationToken
                        )
                        .ConfigureAwait(false);

                    foreach (var routingKey in queue.RoutingKeys)
                    {
                        await RabbitMqQueueTopology
                            .BindQueueAsync(channel, laneExchange, queue.Name, routingKey, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            },
            cancellationToken
        );

    // AMQP has no passive check for a binding, so the check proves the exchanges and queues exist; setup re-binds
    // idempotently.
    public Task CheckAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
        _RunAsync(
            async channel =>
            {
                foreach (var lane in _Lanes)
                {
                    await RabbitMqQueueTopology
                        .RequireExchangeAsync(channel, _Exchange(lane), cancellationToken)
                        .ConfigureAwait(false);
                }

                foreach (var name in Queues(topology).SelectMany(_WithDeadLetter))
                {
                    _ = await RabbitMqQueueTopology
                        .RequireQueueAsync(channel, name, cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken
        );

    public Task ClearAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
        _RunAsync(
            async channel =>
            {
                foreach (var name in _OwnedQueues(topology))
                {
                    _ = await RabbitMqQueueTopology
                        .RequireAsync("queue", name, () => channel.QueuePurgeAsync(name, cancellationToken))
                        .ConfigureAwait(false);
                }
            },
            cancellationToken
        );

    // Deletes only the queues this host consumes, and their dead-letter queues; deleting one that does not exist
    // succeeds, so a half torn-down topology finishes cleanly. The lane exchanges and their dead-letter exchanges stay:
    // ExchangeName is shared by every service on the broker by default, every host requires both lane exchanges, and a
    // publisher binds nothing, so no broker-side check (if-unused sees bindings only) can prove another service is not
    // using one.
    public Task TeardownAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
        _RunAsync(
            async channel =>
            {
                foreach (var name in _OwnedQueues(topology))
                {
                    await channel.QueueDeleteAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken
        );

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
        MessagingTopology topology,
        CancellationToken cancellationToken
    )
    {
        var status = new List<KeyValuePair<string, string>>
        {
            new("exchanges", $"{_Exchange(MessageLane.Bus)}, {_Exchange(MessageLane.Queue)}"),
        };

        await _RunAsync(
                async channel =>
                {
                    foreach (var name in Queues(topology).SelectMany(_WithDeadLetter))
                    {
                        var declared = await RabbitMqQueueTopology
                            .RequireQueueAsync(channel, name, cancellationToken)
                            .ConfigureAwait(false);
                        status.Add(new(name, $"{declared.MessageCount} messages, {declared.ConsumerCount} consumers"));
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return status;
    }

    /// <summary>
    /// The shared queues, in a stable order: one per competing subscription (a Bus identity's queue binds every message
    /// it consumes) and one per registered Queue-lane message, which a publisher may send to before any consumer runs.
    /// A queue is <see cref="RabbitMqTopologyQueue.Owned"/> only when this host consumes it; a queue it only publishes to
    /// belongs to the consuming service.
    /// </summary>
    internal static List<RabbitMqTopologyQueue> Queues(MessagingTopology topology)
    {
        var queues = new SortedDictionary<string, RabbitMqTopologyQueue>(StringComparer.Ordinal);

        foreach (var subscription in topology.Subscriptions)
        {
            if (subscription.Kind is ConsumerSubscriptionKind.EveryInstance)
            {
                continue;
            }

            foreach (var messageName in subscription.MessageNames)
            {
                _Add(
                    queues,
                    RabbitMqPhysicalAddress.Queue(subscription.Lane, subscription.SubscriptionName, messageName),
                    subscription.Lane,
                    messageName,
                    owned: true
                );
            }
        }

        foreach (var message in topology.Messages.Where(static m => m.Lane is MessageLane.Queue))
        {
            _Add(
                queues,
                RabbitMqPhysicalAddress.Queue(MessageLane.Queue, message.MessageName, message.MessageName),
                MessageLane.Queue,
                message.MessageName,
                owned: false
            );
        }

        return [.. queues.Values];
    }

    private static void _Add(
        SortedDictionary<string, RabbitMqTopologyQueue> queues,
        string name,
        MessageLane lane,
        string messageName,
        bool owned
    )
    {
        if (!queues.TryGetValue(name, out var queue))
        {
            queue = new RabbitMqTopologyQueue(name, lane, new SortedSet<string>(StringComparer.Ordinal));
            queues[name] = queue;
        }

        queue.Owned |= owned;
        queue.RoutingKeys.Add(RabbitMqPhysicalAddress.RoutingKey(lane, messageName));
    }

    private IEnumerable<string> _OwnedQueues(MessagingTopology topology) =>
        Queues(topology).Where(static queue => queue.Owned).SelectMany(_WithDeadLetter);

    private IEnumerable<string> _WithDeadLetter(RabbitMqTopologyQueue queue)
    {
        yield return queue.Name;

        if (_options.QueueArguments.EnableDeadLettering)
        {
            yield return RabbitMqQueueTopology.DeadLetterQueue(queue.Name);
        }
    }

    private string _Exchange(MessageLane lane) => RabbitMqPhysicalAddress.Exchange(pool.Exchange, lane);

    // A channel of its own: the broker closes a channel on any refused declare, and a pooled publish channel must not be
    // the one that dies.
    private async Task _RunAsync(Func<IChannel, Task> action, CancellationToken cancellationToken)
    {
        var connection = await pool.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        await using (channel.ConfigureAwait(false))
        {
            await action(channel).ConfigureAwait(false);
        }
    }
}

/// <summary>One shared queue the topology needs, with the routing keys that bind it to its lane exchange.</summary>
internal sealed record RabbitMqTopologyQueue(string Name, MessageLane Lane, SortedSet<string> RoutingKeys)
{
    /// <summary>
    /// Whether this host consumes the queue. Clear and teardown act only on owned queues and their dead-letter queues;
    /// a queue this host only publishes to is set up and checked, never purged or deleted.
    /// </summary>
    public bool Owned { get; set; }
}
