// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// This node's stream epoch: a random token minted when the node starts, which prefixes every event id the node
/// sends as <c>{epoch}:{sequence}</c>.
/// </summary>
/// <remarks>
/// Each node keeps its own event buffer, so a sequence means nothing on another node or after a restart. A reconnect
/// whose last event id names another epoch therefore gets <c>reset</c>, which a load balancer that routes the
/// reconnect to another node needs no sticky routing to honor.
/// </remarks>
internal sealed class NodeEpoch
{
    /// <summary>Gets the epoch token: 12 lower-case hex characters.</summary>
    public string Value { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Formats the event id of <paramref name="sequence"/> on this node.</summary>
    public string FormatId(long sequence) => Value + ":" + sequence.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the sequence from <paramref name="eventId"/> when it names this node's epoch.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <paramref name="eventId"/> is an id this node formatted; otherwise
    /// <see langword="false"/>, for another node's or an earlier run's id, or one that is malformed.
    /// </returns>
    public bool TryParseId(string? eventId, out long sequence)
    {
        sequence = 0;

        if (string.IsNullOrEmpty(eventId))
        {
            return false;
        }

        var separator = eventId.LastIndexOf(':');

        return separator > 0
            && string.Equals(eventId[..separator], Value, StringComparison.Ordinal)
            && long.TryParse(
                eventId.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sequence
            );
    }
}
