// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging.Internal;
using Headless.Messaging.Processor;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// What the consumer view shows of a sequential consumer: which host takes its deliveries in now, and how many
/// messages wait to run on that host.
/// </summary>
/// <remarks>
/// <para>
/// When the messaging distributed lock elects the host, the lock store is the authority on whether the subscription
/// is held, so every node reads it and names the same holder. The store gives the holder's lease, not its host, so the
/// serving node asks itself and then each node discovery lists which one holds that lease, and takes the holder's
/// waiting count from its answer.
/// </para>
/// <para>
/// A read that cannot decide says so: a lock store that fails or does not answer in time, or a lease no reachable node
/// claims, is <c>unavailable</c> with the reason, never <c>none</c>. Under any other intake no one host holds the
/// subscription, so the answer carries the serving node's own waiting count only.
/// </para>
/// </remarks>
internal sealed class MessagingSequentialStates(
    IServiceProvider services,
    string nodeName,
    MessagingForwarding? forwarding = null
)
{
    /// <summary>The manifest's intake value when the messaging distributed lock elects the one host.</summary>
    internal const string LockIntake = "lock";

    /// <summary>How long the lock store may take to say who holds a subscription.</summary>
    internal static readonly TimeSpan LockReadTimeout = TimeSpan.FromSeconds(3);

    private readonly IServiceProvider _services = Argument.IsNotNull(services);
    private readonly string _nodeName = Argument.IsNotNullOrWhiteSpace(nodeName);

    /// <summary>
    /// The sequential state of <paramref name="identity"/> on <paramref name="lane"/>, whose subscriptions are
    /// <paramref name="subscriptions"/> and whose intake the host manifest reports as <paramref name="intake"/>.
    /// </summary>
    public async Task<MessagingSequentialState> ReadAsync(
        string identity,
        MessageLane lane,
        IReadOnlyList<string> subscriptions,
        string? intake,
        CancellationToken cancellationToken
    )
    {
        if (!string.Equals(intake, LockIntake, StringComparison.Ordinal))
        {
            return new MessagingSequentialState(intake, Holders: [], Waiting: _LocalWaiting(identity));
        }

        var holders = await Task.WhenAll(
                subscriptions.Select(subscription => _ReadHolderAsync(identity, lane, subscription, cancellationToken))
            )
            .ConfigureAwait(false);

        return new MessagingSequentialState(intake, holders, Waiting: null);
    }

    /// <summary>What this node answers a peer: the lease it holds <paramref name="resource"/> under, and its count.</summary>
    public PeerIntakeHold ReadLocal(string resource, string consumer) =>
        new(_services.GetService<SequentialIntakeHolds>()?.LeaseOf(resource), _LocalWaiting(consumer));

    private async Task<MessagingIntakeHolder> _ReadHolderAsync(
        string identity,
        MessageLane lane,
        string subscription,
        CancellationToken cancellationToken
    )
    {
        var lockProvider = _services.GetKeyedService<IDistributedLock>(MessagingKeys.LockProvider);
        var options = _services.GetService<IOptions<MessagingOptions>>();

        if (lockProvider is null || options is null)
        {
            return MessagingIntakeHolder.Unavailable(subscription, "This node has no messaging distributed lock.");
        }

        var resource = MessagingKeys.SequentialIntakeResource(options.Value.Version, lane, subscription);
        DistributedLockInfo? info;

        using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            bounded.CancelAfter(LockReadTimeout);

            try
            {
                info = await lockProvider.GetLockInfoAsync(resource, bounded.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return MessagingIntakeHolder.Unavailable(subscription, "The lock store did not answer in time.");
            }
#pragma warning disable CA1031 // Any failure of the lock store leaves the holder unknown, which the view says.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                return MessagingIntakeHolder.Unavailable(subscription, "The lock store could not be read.");
            }
        }

        if (info is null)
        {
            return new MessagingIntakeHolder(
                subscription,
                MessagingIntakeHolder.None,
                Node: null,
                Waiting: null,
                Reason: null
            );
        }

        var local = ReadLocal(resource, identity);

        if (_IsHolder(local, info))
        {
            return MessagingIntakeHolder.Held(subscription, _nodeName, local.Waiting);
        }

        if (forwarding is not { IsEnabled: true })
        {
            return MessagingIntakeHolder.Unavailable(
                subscription,
                "Another host holds it, and this node does not forward to other nodes."
            );
        }

        var peers = await forwarding.Directory.GetPeersAsync(cancellationToken).ConfigureAwait(false);
        var answers = await Task.WhenAll(
                peers.Select(peer => forwarding.GetIntakeHoldAsync(peer, resource, identity, cancellationToken))
            )
            .ConfigureAwait(false);

        for (var i = 0; i < peers.Count; i++)
        {
            if (answers[i] is { } answer && _IsHolder(answer, info))
            {
                return MessagingIntakeHolder.Held(subscription, peers[i].Name, answer.Waiting);
            }
        }

        return MessagingIntakeHolder.Unavailable(
            subscription,
            answers.Any(static answer => answer is null)
                ? "Another host holds it, and a node that may hold it did not answer."
                : "Another host holds it that no listed node claims."
        );
    }

    // A store that cannot name the lease on its inspection path still says the resource is held, so a node that holds
    // it under any lease is the holder.
    private static bool _IsHolder(PeerIntakeHold answer, DistributedLockInfo info) =>
        answer.LeaseId is not null
        && (info.LeaseId is null || string.Equals(answer.LeaseId, info.LeaseId, StringComparison.Ordinal));

    private int? _LocalWaiting(string consumer) =>
        (_services.GetService<IDispatcher>() as Dispatcher)?.SequentialWaiting(consumer);
}

/// <summary>A sequential consumer's state, as the consumer view reads it.</summary>
/// <param name="Intake">How hosts share it, as the host manifest's <c>activeHost</c> reports it.</param>
/// <param name="Holders">The host that holds each of its subscriptions, when the messaging lock elects one.</param>
/// <param name="Waiting">The serving node's waiting count, when no one host holds the subscription.</param>
internal sealed record MessagingSequentialState(
    string? Intake,
    IReadOnlyList<MessagingIntakeHolder> Holders,
    int? Waiting
);

/// <summary>Who holds one subscription of a sequential consumer.</summary>
/// <param name="Subscription">The subscription: the consumer identity on the Bus lane, the message on the Queue lane.</param>
/// <param name="State"><c>held</c>, <c>none</c>, or <c>unavailable</c>.</param>
/// <param name="Node">The holder's node, when held.</param>
/// <param name="Waiting">The messages waiting to run on the holder, when held and the holder reports them.</param>
/// <param name="Reason">Why the holder is unknown, when unavailable.</param>
internal sealed record MessagingIntakeHolder(
    string Subscription,
    string State,
    string? Node,
    int? Waiting,
    string? Reason
)
{
    public const string HeldState = "held";
    public const string None = "none";
    public const string UnavailableState = "unavailable";

    public static MessagingIntakeHolder Held(string subscription, string node, int? waiting) =>
        new(subscription, HeldState, node, waiting, Reason: null);

    public static MessagingIntakeHolder Unavailable(string subscription, string reason) =>
        new(subscription, UnavailableState, Node: null, Waiting: null, reason);
}

/// <summary>A node's answer to whether it holds a sequential intake lock.</summary>
/// <param name="LeaseId">The lease it holds the lock under, or <see langword="null"/> when it does not hold it.</param>
/// <param name="Waiting">The consumer's waiting count on the node, or <see langword="null"/> when it runs no dispatcher.</param>
internal sealed record PeerIntakeHold(string? LeaseId, int? Waiting);
