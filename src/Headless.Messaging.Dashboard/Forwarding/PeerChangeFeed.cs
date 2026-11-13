// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The changes this node serves to a node that relays it: the rows of its stores, and its circuit breakers, which no
/// store holds.
/// </summary>
/// <remarks>
/// The cursor's text form is <c>p1;{circuit position};{store cursor}</c>. This node's own streams need only the store
/// part, because its circuit changes are published to them as they happen.
/// </remarks>
internal sealed class PeerChangeFeed(MessagingChangeSource stores, MessagingCircuitChanges? circuits)
{
    private const string _Prefix = "p1;";
    private const char _Separator = ';';

    private readonly MessagingChangeSource _stores = Argument.IsNotNull(stores);

    public async Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        // Read first, so a circuit change made while the stores answer is still after the position.
        var position = circuits?.Position ?? 0;

        return _Format(position, await _stores.GetStartCursorAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Reads the row and circuit changes after <paramref name="cursor"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="cursor"/> is not a peer change cursor.</exception>
    public async Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        if (!_TryParse(cursor, out var position, out var storeCursor))
        {
            throw new ArgumentException($"'{cursor}' is not a peer change cursor.", nameof(cursor));
        }

        var batch = await _stores.ReadChangesAsync(storeCursor, cancellationToken).ConfigureAwait(false);
        var (changes, next) = circuits?.ReadSince(position) ?? ([], position);

        return batch with
        {
            Changes = changes.Count == 0 ? batch.Changes : [.. batch.Changes, .. changes],
            Cursor = _Format(next, batch.Cursor),
        };
    }

    private static string _Format(long position, string storeCursor)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{_Prefix}{position}{_Separator}{storeCursor}");
    }

    private static bool _TryParse(string cursor, out long position, out string storeCursor)
    {
        position = 0;
        storeCursor = string.Empty;

        if (!cursor.StartsWith(_Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separator = cursor.IndexOf(_Separator, _Prefix.Length);

        if (
            separator < 0
            || !long.TryParse(
                cursor.AsSpan(_Prefix.Length, separator - _Prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out position
            )
        )
        {
            return false;
        }

        storeCursor = cursor[(separator + 1)..];

        return true;
    }
}
