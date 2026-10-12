// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Nats;

/// <summary>
/// Maps the subject a delivery arrived on to the message name whose subscription it belongs to, from the same subject
/// function the client filters with. A subject is never parsed back into a name: a dotted message name and a shard token
/// both add subject tokens, so the subject alone does not say where the name ends.
/// </summary>
internal sealed class NatsArrivalNames
{
    /// <summary>A lookup for a client that subscribed to nothing: no subject names a message.</summary>
    public static NatsArrivalNames Empty { get; } =
        new(FrozenDictionary<string, string>.Empty, FrozenDictionary<string, string>.Empty, [], []);

    // A subscribed message's own subject.
    private readonly FrozenDictionary<string, string> _exact;

    // A sharded subscribed message's own subject, under which one shard token follows.
    private readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _shardParents;

    // The own subject of every message on the lane, subscribed or not, so a sharded wildcard that also matches another
    // message's subject does not claim that message.
    private readonly FrozenSet<string> _laneSubjects;

    // The subscribed external subject bindings. Their filters never overlap one another or 'headless.>' (startup refuses
    // both), so a subject matches at most one of them and never a derived subject.
    private readonly NatsExternalBinding[] _externals;

    private NatsArrivalNames(
        FrozenDictionary<string, string> exact,
        FrozenDictionary<string, string> shardParents,
        FrozenSet<string> laneSubjects,
        NatsExternalBinding[] externals
    )
    {
        _exact = exact;
        _shardParents = shardParents.GetAlternateLookup<ReadOnlySpan<char>>();
        _laneSubjects = laneSubjects;
        _externals = externals;
    }

    /// <summary>Builds the lookup of a client's subscriptions.</summary>
    /// <param name="lane">The lane the client subscribes on.</param>
    /// <param name="subscribedNames">The message names the client subscribed to, as registered.</param>
    /// <param name="shardedNames">The subscribed names the client also filters on the shard wildcard of.</param>
    /// <param name="laneNames">Every message name registered on <paramref name="lane"/>.</param>
    /// <param name="externals">The subscribed names bound to an external subject, which filter on it instead.</param>
    public static NatsArrivalNames Create(
        MessageLane lane,
        IEnumerable<string> subscribedNames,
        ISet<string> shardedNames,
        IEnumerable<string> laneNames,
        IEnumerable<NatsExternalBinding>? externals = null
    )
    {
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        var shardParents = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in subscribedNames)
        {
            var subject = NatsPhysicalAddress.Subject(lane, name);
            exact.TryAdd(subject, name);

            if (shardedNames.Contains(name))
            {
                shardParents.TryAdd(subject, name);
            }
        }

        var laneSubjects = new HashSet<string>(exact.Keys, StringComparer.Ordinal);
        foreach (var name in laneNames)
        {
            laneSubjects.Add(NatsPhysicalAddress.Subject(lane, name));
        }

        return new NatsArrivalNames(
            exact.ToFrozenDictionary(StringComparer.Ordinal),
            shardParents.ToFrozenDictionary(StringComparer.Ordinal),
            laneSubjects.ToFrozenSet(StringComparer.Ordinal),
            externals?.ToArray() ?? []
        );
    }

    /// <summary>
    /// Describes where a delivery on <paramref name="subject"/> arrived: the subscribed message it belongs to and, for an
    /// external subject binding, that the subject names the contract and carries its tenant.
    /// </summary>
    public EnvelopeArrival Arrival(string transport, MessageLane lane, string subject)
    {
        if (Resolve(subject) is { } name)
        {
            return new EnvelopeArrival(transport, lane, name);
        }

        foreach (var external in _externals)
        {
            if (NatsStreamReconciliation.Matches(external.Filter, subject))
            {
                var (tenant, tenantError) = external.Tenant(subject);

                return new EnvelopeArrival(transport, lane, external.MessageName)
                {
                    AddressIsAuthoritative = true,
                    AddressTenant = tenant,
                    AddressTenantError = tenantError,
                };
            }
        }

        return new EnvelopeArrival(transport, lane, ArrivalName: null);
    }

    /// <summary>
    /// Returns the subscribed message name <paramref name="subject"/> belongs to, or <see langword="null"/> when it names
    /// no single subscribed message.
    /// </summary>
    public string? Resolve(string subject)
    {
        if (_exact.TryGetValue(subject, out var name))
        {
            return name;
        }

        // A shard is one token, so only the last token can be one.
        var lastDot = subject.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == subject.Length - 1)
        {
            return null;
        }

        if (!_shardParents.TryGetValue(subject.AsSpan(0, lastDot), out var parent))
        {
            return null;
        }

        return _laneSubjects.Contains(subject) ? null : parent;
    }
}
