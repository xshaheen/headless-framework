// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using DotPulsar.Exceptions;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Remembers the messages the client refused as larger than the broker's maximum message size, so a send whose producer
/// faulted can tell a refusal of its own message from any other fault.
/// </summary>
/// <remarks>
/// DotPulsar faults the whole producer when one message is too large, and reports why only to the client's exception
/// handler, which names no producer. A refusal is matched to a send by size: DotPulsar measures the serialized frame,
/// which is the payload plus its framing, command, and metadata, so the refused size falls just above the payload.
/// </remarks>
internal sealed class PulsarSizeRefusals(TimeProvider timeProvider)
{
    /// <summary>The most a frame adds to a payload: framing, the send command, and the message metadata.</summary>
    internal const int MaxFrameOverhead = 64 * 1024;

    // A refusal older than this belongs to a send that already failed or gave up.
    private static readonly TimeSpan _Retention = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private readonly List<(long At, TooLargeMessageException Refusal)> _refusals = [];

    /// <summary>Records a refusal the client reported.</summary>
    public void Record(TooLargeMessageException refusal)
    {
        var now = timeProvider.GetTimestamp();
        lock (_lock)
        {
            _Prune(now);
            _refusals.Add((now, refusal));
        }
    }

    /// <summary>
    /// Takes the refusal of a message with <paramref name="payloadLength"/> payload bytes, if the client refused one.
    /// </summary>
    public bool TryTake(long payloadLength, [NotNullWhen(true)] out TooLargeMessageException? refusal)
    {
        var now = timeProvider.GetTimestamp();
        lock (_lock)
        {
            _Prune(now);
            for (var i = 0; i < _refusals.Count; i++)
            {
                var candidate = _refusals[i].Refusal;
                var overhead = candidate.MessageSize - payloadLength;
                if (overhead is >= 0 and <= MaxFrameOverhead)
                {
                    _refusals.RemoveAt(i);
                    refusal = candidate;
                    return true;
                }
            }
        }

        refusal = null;
        return false;
    }

    private void _Prune(long now)
    {
        _refusals.RemoveAll(entry => timeProvider.GetElapsedTime(entry.At, now) > _Retention);
    }
}
