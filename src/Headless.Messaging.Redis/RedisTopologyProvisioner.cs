// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Hosting;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// Provisions the shared Redis Streams topology: one consumer group, created with its stream, per stream a competing
/// subscription reads and per registered Queue-lane message.
/// </summary>
/// <remarks>
/// A Bus stream is shared: every consumer identity that subscribes to the message reads it through a group of its own.
/// So clearing and teardown act on this host's groups, never on the stream as a whole. Per-process objects stay the
/// running host's: an every-instance consumer reads without a group, request/reply uses pub/sub, and a subscription an
/// <c>IRuntimeSubscriber</c> adds is not in the topology at all.
/// </remarks>
internal sealed class RedisTopologyProvisioner(IRedisConnectionPool pool, IOptions<RedisMessagingOptions> options)
    : ITransportTopologyProvisioner
{
    // Destroys the group, then deletes the stream only when no other group reads it, in one atomic step: a separate
    // check could delete a stream another service's group was created on in between.
    private const string _DeleteGroupScript = """
        redis.pcall('XGROUP', 'DESTROY', KEYS[1], ARGV[1])
        local groups = redis.pcall('XINFO', 'GROUPS', KEYS[1])
        if type(groups) == 'table' and not groups.err and #groups == 0 then
            redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    // Resets the group only when it exists, in one atomic step, so a consumer never reads between the destroy and the
    // create and a purge never creates a group setup did not. RESP2 returns each XINFO GROUPS row as a flat name/value
    // list.
    private const string _PurgeGroupScript = """
        local groups = redis.pcall('XINFO', 'GROUPS', KEYS[1])
        if type(groups) ~= 'table' or groups.err then
            return 0
        end
        local found = false
        for _, group in ipairs(groups) do
            for i = 1, #group, 2 do
                if group[i] == 'name' and group[i + 1] == ARGV[1] then
                    found = true
                end
            end
        end
        if not found then
            return 0
        end
        if ARGV[2] == '1' then
            redis.call('XTRIM', KEYS[1], 'MAXLEN', 0)
        end
        redis.call('XGROUP', 'DESTROY', KEYS[1], ARGV[1])
        redis.call('XGROUP', 'CREATE', KEYS[1], ARGV[1], '$')
        return 1
        """;

    private readonly string _hostPrefix = options.Value.HostPrefix;

    public string Kind => SetupMessagingRedis.TopologyResourceKind;

    public async Task ProvisionAsync(MessagingTopology topology, CancellationToken cancellationToken)
    {
        var database = await _GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        foreach (var group in Groups(topology, _hostPrefix))
        {
            await database
                .EnsureConsumerGroupAsync(group.Stream, group.Group)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // XINFO GROUPS only reads, and the checks share one batch: one round trip to each node, whatever the stream count.
    public async Task VerifyAsync(MessagingTopology topology, CancellationToken cancellationToken)
    {
        var groups = Groups(topology, _hostPrefix);
        if (groups.Count == 0)
        {
            return;
        }

        var database = await _GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var existing = await _ReadGroupsAsync(database, groups, cancellationToken).ConfigureAwait(false);

        var missing = groups.Find(group =>
            !existing[group.Stream].Any(info => string.Equals(info.Name, group.Group, StringComparison.Ordinal))
        );
        if (missing is not null)
        {
            throw RedisStreamManagerExtensions.MissingConsumerGroup(missing.Group, missing.Stream);
        }
    }

    // A Bus stream holds the entries of every consumer identity that subscribes to the message, so trimming it would
    // drop what other services have yet to read. Re-creating this host's group at the stream's end instead skips its
    // backlog and drops its pending entries, and leaves every other group as it was. A Queue-lane stream is read only
    // through the group this host owns, so its entries are trimmed too.
    public async Task PurgeAsync(MessagingTopology topology, CancellationToken cancellationToken)
    {
        var database = await _GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        foreach (var group in _OwnedGroups(topology))
        {
            RedisValue trim = group.Lane is MessageLane.Queue ? 1 : 0;
            var reset = (long)
                await database
                    .ScriptEvaluateAsync(_PurgeGroupScript, [group.Stream], [group.Group, trim])
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (reset == 0)
            {
                throw RedisStreamManagerExtensions.MissingConsumerGroup(group.Group, group.Stream);
            }
        }
    }

    // Deletes only the groups this host consumes through, and a stream once no group reads it any more; deleting a group
    // that does not exist succeeds, so a half torn-down topology finishes cleanly. A group this host only publishes to
    // belongs to the consuming service and stays.
    public async Task DeleteAsync(MessagingTopology topology, CancellationToken cancellationToken)
    {
        var database = await _GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        foreach (var group in _OwnedGroups(topology))
        {
            await database
                .ScriptEvaluateAsync(_DeleteGroupScript, [group.Stream], [group.Group])
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
        MessagingTopology topology,
        CancellationToken cancellationToken
    )
    {
        var groups = Groups(topology, _hostPrefix);
        if (groups.Count == 0)
        {
            return [];
        }

        var database = await _GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        var existing = await _ReadGroupsAsync(database, groups, cancellationToken).ConfigureAwait(false);

        return
        [
            .. groups.Select(group =>
            {
                var info = existing[group.Stream]
                    .FirstOrDefault(info => string.Equals(info.Name, group.Group, StringComparison.Ordinal));
                var state = info.Name is null
                    ? "missing"
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"{info.ConsumerCount} consumers, {info.PendingMessageCount} pending"
                    );

                return new KeyValuePair<string, string>($"{group.Stream} {group.Group}", state);
            }),
        ];
    }

    /// <summary>
    /// The shared consumer groups, in a stable order: one per stream a competing subscription reads (a Bus identity's
    /// group on each message's stream) and one per registered Queue-lane message, which a publisher may add to before any
    /// consumer runs and which keeps its entries for <see cref="RedisMessagingOptions.StreamMaxAge"/> only once a group
    /// exists. A group is <see cref="RedisTopologyGroup.Owned"/> only when this host consumes through it; a group it only
    /// publishes to belongs to the consuming service.
    /// </summary>
    internal static List<RedisTopologyGroup> Groups(MessagingTopology topology, string hostPrefix = "")
    {
        var groups = new SortedDictionary<(string Stream, string Group), RedisTopologyGroup>();

        foreach (var subscription in topology.Subscriptions)
        {
            if (subscription.Kind is ConsumerSubscriptionKind.EveryInstance)
            {
                continue;
            }

            var group = RedisPhysicalAddress.ConsumerGroup(subscription.Lane, subscription.SubscriptionName);

            foreach (var messageName in subscription.MessageNames)
            {
                var stream = RedisPhysicalAddress.ForLane(hostPrefix, subscription.Lane, messageName);
                _Add(groups, stream, group, subscription.Lane, owned: true);
            }
        }

        foreach (var message in topology.Messages.Where(static m => m.Lane is MessageLane.Queue))
        {
            _Add(
                groups,
                RedisPhysicalAddress.ForLane(hostPrefix, MessageLane.Queue, message.MessageName),
                RedisPhysicalAddress.ConsumerGroup(MessageLane.Queue, message.MessageName),
                MessageLane.Queue,
                owned: false
            );
        }

        return [.. groups.Values];
    }

    private static void _Add(
        SortedDictionary<(string Stream, string Group), RedisTopologyGroup> groups,
        string stream,
        string group,
        MessageLane lane,
        bool owned
    )
    {
        if (!groups.TryGetValue((stream, group), out var existing))
        {
            existing = new RedisTopologyGroup(stream, group, lane);
            groups[(stream, group)] = existing;
        }

        existing.Owned |= owned;
    }

    // Reads each distinct stream's groups in one batch; an absent stream reads as having none.
    private static async Task<Dictionary<string, StreamGroupInfo[]>> _ReadGroupsAsync(
        IDatabase database,
        List<RedisTopologyGroup> groups,
        CancellationToken cancellationToken
    )
    {
        var batch = database.CreateBatch();
        var reads = groups
            .Select(static group => group.Stream)
            .Distinct(StringComparer.Ordinal)
            .Select(stream => (Stream: stream, Read: batch.StreamGroupInfoAsync(stream)))
            .ToList();
        batch.Execute();

        var result = new Dictionary<string, StreamGroupInfo[]>(StringComparer.Ordinal);

        foreach (var (stream, read) in reads)
        {
            try
            {
                result[stream] = await read.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RedisServerException ex) when (ex.GetRedisErrorType() == RedisErrorTypes.NoGroupInfoExists)
            {
                result[stream] = [];
            }
        }

        return result;
    }

    private IEnumerable<RedisTopologyGroup> _OwnedGroups(MessagingTopology topology) =>
        Groups(topology, _hostPrefix).Where(static group => group.Owned);

    private async Task<IDatabase> _GetDatabaseAsync(CancellationToken cancellationToken)
    {
        var connection = await pool.ConnectAsync(cancellationToken).ConfigureAwait(false);

        return connection.GetDatabase();
    }
}

/// <summary>One shared consumer group the topology needs, on the stream it reads.</summary>
internal sealed record RedisTopologyGroup(string Stream, string Group, MessageLane Lane)
{
    /// <summary>
    /// Whether this host consumes through the group. Clear and teardown act only on owned groups; a group this host only
    /// publishes to is set up and checked, never purged or deleted.
    /// </summary>
    public bool Owned { get; set; }
}
