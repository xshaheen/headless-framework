// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>
/// One durable a competing consumer client pulls from: its name, the subjects it filters on, and, on the Bus lane, the
/// per-subject durables earlier releases created for the same subjects, which the first bind takes over.
/// </summary>
internal sealed record NatsDurableBinding(
    string Durable,
    IReadOnlyList<string> Filters,
    IReadOnlyList<string> LegacyDurables
);

/// <summary>Creates or binds a competing consumer's durable, taking over from the per-subject Bus durables.</summary>
internal static class NatsDurableBinder
{
    private const int _ConsumerNotFound = 10014;
    private const int _ConsumerNameInUse = 10013;
    private const int _ConsumerAlreadyExists = 10148;

    /// <summary>
    /// Returns the subjects one consumer filters on: a subject another one covers is dropped, because JetStream refuses a
    /// consumer whose filters overlap and the covering filter already delivers it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two filters overlap without one covering the other.</exception>
    public static List<string> MergeFilters(IEnumerable<string> subjects)
    {
        var distinct = subjects.Distinct(StringComparer.Ordinal).ToList();

        var kept = distinct
            .Where(subject =>
                !distinct.Exists(other =>
                    !string.Equals(other, subject, StringComparison.Ordinal)
                    && NatsStreamReconciliation.Covers(other, subject)
                )
            )
            .ToList();

        for (var i = 0; i < kept.Count; i++)
        {
            for (var j = i + 1; j < kept.Count; j++)
            {
                if (NatsStreamReconciliation.Overlaps(kept[i], kept[j]))
                {
                    throw new InvalidOperationException(
                        $"NATS consumer filters '{kept[i]}' and '{kept[j]}' overlap, and JetStream refuses overlapping "
                            + "filters on one consumer. Bind the external subjects so one filter covers the other or "
                            + "neither matches the other's subjects."
                    );
                }
            }
        }

        return kept;
    }

    /// <summary>
    /// Binds the durable <paramref name="config"/> names. A Queue durable (no legacy durables) is created or updated as
    /// configured. A Bus durable keeps the start position it was created with, because JetStream refuses an update that
    /// changes it; when it does not exist yet, it starts where the slowest legacy per-subject durable stopped, and those
    /// durables are deleted.
    /// </summary>
    public static async Task<INatsJSConsumer> BindAsync(
        INatsJSContext js,
        string stream,
        ConsumerConfig config,
        IReadOnlyList<string> legacyDurables,
        Action<string> warn,
        CancellationToken cancellationToken
    )
    {
        if (legacyDurables.Count == 0)
        {
            return _Checked(
                await js.CreateOrUpdateConsumerAsync(stream, config, cancellationToken).ConfigureAwait(false),
                config
            );
        }

        var name = config.Name!;

        // A second pass covers a concurrent first bind: another client of the identity, in this process or another
        // replica, can create the durable between the lookup and the create.
        for (var attempt = 0; ; attempt++)
        {
            if (await _FindAsync(js, stream, name, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                var existingConfig = existing.Info.Config;
                var kept = config with
                {
                    DeliverPolicy = existingConfig.DeliverPolicy,
                    OptStartSeq = existingConfig.OptStartSeq,
                    OptStartTime = existingConfig.OptStartTime,
                };

                return _Checked(
                    await js.CreateOrUpdateConsumerAsync(stream, kept, cancellationToken).ConfigureAwait(false),
                    kept
                );
            }

            var start = await HandOffSequenceAsync(js, stream, legacyDurables, cancellationToken).ConfigureAwait(false);
            var create = start is { } sequence
                ? config with
                {
                    DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
                    OptStartSeq = sequence,
                }
                : config;

            INatsJSConsumer created;
            try
            {
                created = await js.CreateConsumerAsync(stream, create, cancellationToken).ConfigureAwait(false);
            }
            catch (NatsJSApiException ex)
                when (attempt == 0 && ex.Error.ErrCode is _ConsumerAlreadyExists or _ConsumerNameInUse)
            {
                continue;
            }

            _Checked(created, create);

            if (start is { } handedOff)
            {
                warn(
                    $"NATS Bus durable '{name}' on stream '{stream}' replaces the per-message durables "
                        + $"{string.Join(", ", legacyDurables.Select(d => $"'{d}'"))}: it starts at stream sequence "
                        + $"{handedOff.ToString(CultureInfo.InvariantCulture)}, where the slowest of them stopped, "
                        + "and the inbox drops what they already delivered."
                );
            }

            await _DeleteLegacyAsync(js, stream, legacyDurables, warn, cancellationToken).ConfigureAwait(false);

            return created;
        }
    }

    /// <summary>
    /// Returns the stream sequence a new per-stream Bus durable starts at so it misses nothing the legacy per-subject
    /// durables had not settled, or <see langword="null"/> when none of them exists. A legacy durable with nothing
    /// pending or unacknowledged stopped at the stream's last sequence, read before it so a message stored in between
    /// still counts as pending; any other stopped at its acknowledgement floor. The earliest of those wins.
    /// </summary>
    public static async Task<ulong?> HandOffSequenceAsync(
        INatsJSContext js,
        string stream,
        IReadOnlyList<string> legacyDurables,
        CancellationToken cancellationToken
    )
    {
        // Read before any durable, so a durable with nothing pending has delivered everything up to this sequence.
        var lastSequence = await _LastSequenceAsync(js, stream, cancellationToken).ConfigureAwait(false);
        ulong? floor = null;

        foreach (var legacy in legacyDurables)
        {
            if (await _FindAsync(js, stream, legacy, cancellationToken).ConfigureAwait(false) is not { } consumer)
            {
                continue;
            }

            var info = consumer.Info;
            var stoppedAt = info is { NumPending: 0, NumAckPending: 0 } ? lastSequence : info.AckFloor.StreamSeq;

            floor = floor is null ? stoppedAt : Math.Min(floor.Value, stoppedAt);
        }

        return floor + 1;
    }

    private static async Task<ulong> _LastSequenceAsync(
        INatsJSContext js,
        string stream,
        CancellationToken cancellationToken
    )
    {
        var info = await js.GetStreamAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return info.Info.State.LastSeq;
    }

    private static async Task _DeleteLegacyAsync(
        INatsJSContext js,
        string stream,
        IReadOnlyList<string> legacyDurables,
        Action<string> warn,
        CancellationToken cancellationToken
    )
    {
        foreach (var legacy in legacyDurables)
        {
            try
            {
                await js.DeleteConsumerAsync(stream, legacy, cancellationToken).ConfigureAwait(false);
            }
            catch (NatsJSApiException ex) when (_IsNotFound(ex))
            {
                // Never created, or another client deleted it first.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The new durable already delivers these subjects; a legacy durable left behind only holds its
                // pending messages on an interest-retained stream until an operator removes it.
                warn(
                    $"NATS could not delete the per-message durable '{legacy}' on stream '{stream}' after its "
                        + $"per-stream replacement took over; remove it with 'nats consumer rm {stream} {legacy}': {ex.Message}"
                );
            }
        }
    }

    private static async Task<INatsJSConsumer?> _FindAsync(
        INatsJSContext js,
        string stream,
        string durable,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await js.GetConsumerAsync(stream, durable, cancellationToken).ConfigureAwait(false);
        }
        catch (NatsJSApiException ex) when (_IsNotFound(ex))
        {
            return null;
        }
    }

    private static bool _IsNotFound(NatsJSApiException ex) => ex.Error.ErrCode == _ConsumerNotFound;

    // A server before 2.10 does not know FilterSubjects and drops it, which leaves a consumer of the whole stream that
    // would hand this client every message on it.
    private static INatsJSConsumer _Checked(INatsJSConsumer consumer, ConsumerConfig requested)
    {
        if (requested.FilterSubjects is not { Count: > 0 } wanted)
        {
            return consumer;
        }

        var echoed = consumer.Info?.Config.FilterSubjects;
        if (echoed is null || !echoed.ToHashSet(StringComparer.Ordinal).SetEquals(wanted))
        {
            throw new InvalidOperationException(
                $"NATS server ignored the filter subjects of consumer '{requested.Name}'. A Bus consumer of several "
                    + "messages on one stream filters on all of them in one durable, which needs NATS server 2.10 or later."
            );
        }

        return consumer;
    }
}
