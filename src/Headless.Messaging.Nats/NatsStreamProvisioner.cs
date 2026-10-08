// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>
/// Creates, verifies, or reconciles the JetStream streams that Headless subjects live on, under
/// <see cref="NatsMessagingOptions.StreamProvisioning"/>. Consumer clients run it at startup and the transport runs it
/// before the first publish of each message, so a publish-only host never fails with an unacknowledged publish.
/// </summary>
internal sealed class NatsStreamProvisioner(IOptions<NatsMessagingOptions> options)
{
    private readonly NatsMessagingOptions _options = Argument.IsNotNull(options.Value);

    // One ensure per message, lane, and sharding per process. A Lazy keeps concurrent first publishes on one broker
    // round trip, and a failed or abandoned ensure is evicted so the next publish retries it instead of inheriting the
    // failure. Sharding is part of the key because a name the stream key does not prefix needs `{name}.>` only once
    // it is published with a shard, and an unsharded first publish must not stand in for that ensure.
    private readonly ConcurrentDictionary<
        (MessageLane Lane, string MessageName, bool IsSharded),
        Lazy<Task>
    > _published = new();

    public bool IsEnabled => _options.StreamProvisioning is not NatsStreamProvisioning.Disabled;

    /// <summary>
    /// Returns the logical subjects (without the lane prefix) a stream must carry for <paramref name="messageNames"/>,
    /// which all normalize to <paramref name="streamKey"/>.
    /// </summary>
    /// <remarks>
    /// A name that extends the key (<c>{key}.…</c>, always true for the default first-segment normalizer) contributes
    /// the key's wildcard, <c>{key}.&gt;</c>, rather than its own subject, which also covers its shards. Every host then
    /// asks for the same subject whichever of those names it publishes or consumes, so the first host to create the
    /// stream covers every later one and start order cannot fail a host under <see cref="NatsStreamProvisioning.Verify"/>.
    /// The bare key is added only for a name equal to it, so an operator stream provisioned as <c>{key}.&gt;</c> verifies
    /// cleanly. A name a custom normalizer maps without that prefix keeps its exact subject, plus <c>{name}.&gt;</c> when
    /// it is sharded.
    /// </remarks>
    public static IReadOnlyList<string> BuildStreamSubjects(
        string streamKey,
        IEnumerable<string> messageNames,
        ISet<string> shardedMessageNames
    )
    {
        Argument.IsNotNullOrWhiteSpace(streamKey);
        Argument.IsNotNull(messageNames);
        Argument.IsNotNull(shardedMessageNames);

        var subjects = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keyPrefix = streamKey + ".";

        foreach (var messageName in messageNames)
        {
            if (messageName.StartsWith(keyPrefix, StringComparison.Ordinal))
            {
                add(keyPrefix + ">");
                continue;
            }

            add(messageName);

            if (shardedMessageNames.Contains(messageName))
            {
                add(messageName + ".>");
            }
        }

        return subjects;

        void add(string subject)
        {
            if (seen.Add(subject))
            {
                subjects.Add(subject);
            }
        }
    }

    /// <summary>Ensures the streams for <paramref name="messageNames"/> on <paramref name="lane"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// A stream diverges from the configuration this host asks for and the mode does not allow fixing it, or
    /// <see cref="NatsMessagingOptions.StreamOptions"/> tried to change provider-owned stream identity.
    /// </exception>
    public async Task EnsureAsync(
        INatsJSContext js,
        MessageLane lane,
        IReadOnlyCollection<string> messageNames,
        ISet<string> shardedMessageNames,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled)
        {
            return;
        }

        foreach (var streamGroup in messageNames.GroupBy(_options.NormalizeStreamName, StringComparer.Ordinal))
        {
            await _EnsureStreamAsync(
                    js,
                    lane,
                    streamGroup.Key,
                    BuildStreamSubjects(streamGroup.Key, streamGroup, shardedMessageNames),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures the stream for one published message, once per message, lane, and sharding per process. A failed
    /// ensure is retried by the next publish.
    /// </summary>
    public async Task EnsureForPublishAsync(
        INatsJSContext js,
        MessageLane lane,
        string messageName,
        bool isSharded,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled)
        {
            return;
        }

        var key = (lane, messageName, isSharded);
        var ensure = _published.GetOrAdd(
            key,
            static (k, state) =>
                new Lazy<Task>(
                    () =>
                        // The ensure is shared by every publisher of this message, so no single caller's token may
                        // cancel it; StreamCreateTimeout bounds it instead.
                        state.Provisioner.EnsureAsync(
                            state.Js,
                            k.Lane,
                            [k.MessageName],
                            k.IsSharded
                                ? new HashSet<string>(StringComparer.Ordinal) { k.MessageName }
                                : new HashSet<string>(StringComparer.Ordinal),
                            CancellationToken.None
                        ),
                    LazyThreadSafetyMode.ExecutionAndPublication
                ),
            (Provisioner: this, Js: js)
        );

        try
        {
            await ensure.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up waiting; the shared ensure keeps running for the next publisher.
            throw;
        }
        catch
        {
            _published.TryRemove(new KeyValuePair<(MessageLane, string, bool), Lazy<Task>>(key, ensure));
            throw;
        }
    }

    /// <summary>Returns the name of the Headless stream that carries <paramref name="messageName"/> on <paramref name="lane"/>.</summary>
    public string StreamName(MessageLane lane, string messageName) =>
        NatsPhysicalAddress.Stream(lane, _options.NormalizeStreamName(messageName));

    /// <summary>Drops the remembered ensure for a published message, so its next publish provisions the stream again.</summary>
    public void ForgetPublished(MessageLane lane, string messageName)
    {
        _published.TryRemove((lane, messageName, false), out _);
        _published.TryRemove((lane, messageName, true), out _);
    }

    private async Task _EnsureStreamAsync(
        INatsJSContext js,
        MessageLane lane,
        string streamKey,
        IReadOnlyList<string> logicalSubjects,
        CancellationToken cancellationToken
    )
    {
        var streamName = NatsPhysicalAddress.Stream(lane, streamKey);
        var subjects = new HashSet<string>(
            logicalSubjects.Select(subject => NatsPhysicalAddress.Subject(lane, subject)),
            StringComparer.Ordinal
        );

        using var cts = _options.StreamCreateTimeout.ToCancellationTokenSource(cancellationToken);

        // Several hosts can normalize to the same stream name, and an update REPLACES the subject list, so union with
        // whatever the stream already carries (from another host, or a pre-provisioned stream) to avoid clobbering it.
        // The probe also decides create-versus-exists, so its result outlives the try block.
        StreamConfig? liveConfig = null;

        try
        {
            var existing = await js.GetStreamAsync(streamName, cancellationToken: cts.Token).ConfigureAwait(false);

            liveConfig = existing.Info.Config;

            if (liveConfig.Subjects is { } existingSubjects)
            {
                subjects.UnionWith(existingSubjects);
            }
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404 || ex.Error.ErrCode == 10059)
        {
            // Stream does not exist yet; it will be created below.
        }

        var expectedSubjects = NatsConsumerClient.PruneOverlappingSubjects(subjects);

        var config = new StreamConfig
        {
            Name = streamName,
            // JetStream rejects a stream whose subject list contains overlapping entries, so drop any
            // exact subject already covered by a '.>' wildcard in the union (e.g. a pre-provisioned
            // 'prefix.>' catch-all subsumes the exact 'prefix.foo' subjects).
            Subjects = [.. expectedSubjects],
            NoAck = false,
            // File storage is the production default. Override via StreamOptions
            // for dev/testing: config.Storage = StreamConfigStorage.Memory;
            Storage = StreamConfigStorage.File,
            Retention = NatsPhysicalAddress.Retention(lane),
        };

        // Snapshot either side of the callback so the comparison below can tell a field the operator
        // asserted from one the server will fill with its own default. Diffing an unasserted field would
        // report drift against every stream that exists.
        var beforeOptions = NatsStreamReconciliation.Snapshot(config);
        _options.StreamOptions?.Invoke(config);
        var assertedFields = NatsStreamReconciliation.AssertedFields(
            beforeOptions,
            NatsStreamReconciliation.Snapshot(config)
        );

        // The provider sets these itself, so they are asserted whether or not the callback touched them.
        assertedFields.Add(nameof(StreamConfig.Storage));
        assertedFields.Add(nameof(StreamConfig.NoAck));
        assertedFields.Add(nameof(StreamConfig.Retention));

        if (
            !string.Equals(config.Name, streamName, StringComparison.Ordinal)
            || config.Retention != NatsPhysicalAddress.Retention(lane)
            || config.Subjects?.ToHashSet(StringComparer.Ordinal).SetEquals(expectedSubjects) != true
        )
        {
            throw new InvalidOperationException(
                $"NATS {lane} stream identity, subjects, and retention are provider-owned. "
                    + "StreamOptions may configure storage, replicas, and limits but cannot override lane topology."
            );
        }

        if (liveConfig is null)
        {
            // First-run creation happens in every enabled mode; only an existing stream is contentious.
            var created = await js.CreateStreamAsync(config, cts.Token).ConfigureAwait(false);
            liveConfig = created.Info.Config;

            // CreateStreamAsync returns an existing same-name stream if another client won the race.
            // Treat that topology exactly like the pre-existing branch: preserve sibling subjects and
            // run the same verification/reconciliation decision instead of assuming this create won.
            if (liveConfig.Subjects is { } racedSubjects)
            {
                subjects.UnionWith(racedSubjects);
                expectedSubjects = NatsConsumerClient.PruneOverlappingSubjects(subjects);
                config.Subjects = [.. expectedSubjects];
            }
        }

        var divergences = new List<StreamDivergence>(
            NatsStreamReconciliation.CompareFields(config, liveConfig, assertedFields)
        );

        // A subject this host needs that the stream does not carry is not cosmetic drift: JetStream
        // delivers nothing, and reports no error, to a consumer filter that matches no subject.
        var uncovered = NatsStreamReconciliation.FindUncoveredSubjects(expectedSubjects, liveConfig.Subjects);

        if (uncovered.Count > 0)
        {
            divergences.Add(
                new StreamDivergence(
                    nameof(StreamConfig.Subjects),
                    string.Join(", ", uncovered),
                    "not carried by the stream",
                    IsImmutable: false
                )
            );
        }

        if (divergences.Count == 0)
        {
            return;
        }

        var reconcilable = divergences.TrueForAll(divergence => !divergence.IsImmutable);

        if (_options.StreamProvisioning is NatsStreamProvisioning.Reconcile && reconcilable)
        {
            await js.UpdateStreamAsync(config, cts.Token).ConfigureAwait(false);

            return;
        }

        // Must stay an InvalidOperationException. ConsumerRegister.ExecuteAsync catches
        // BrokerConnectionException, flips the health flag, and returns, so raising a divergence as one
        // would hide it behind an unhealthy consumer instead of surfacing it. This also matches the
        // lane-identity guard above, which already reports a configuration fault the same way.
        throw new InvalidOperationException(
            NatsStreamReconciliation.ComposeDivergenceMessage(streamName, divergences, _options.StreamProvisioning)
        );
    }
}
