// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Adapts the change reads of every Messaging store to the dashboard's change source: the dashboard cursor holds one
/// storage change cursor per store, and each storage cursor keeps its own position per table, so a slow store neither
/// holds the others back nor loses its place.
/// </summary>
/// <remarks>
/// <para>
/// The cursor's text form is <c>m1|{store}={cursor}|…</c>, the main store first. A store that has not answered yet is
/// written <c>{store}=-</c> and starts from its current position once it does. A store added to the host after a
/// cursor was issued starts from its current position; a store the cursor names that the host no longer has is
/// dropped. The text resumes on any node of the deployment, because every node reads the same stores.
/// </para>
/// <para>
/// A store that throws or does not answer within <see cref="MessagingStores.Timeout"/> keeps its position and is named
/// in <see cref="DashboardChangeBatch.UnavailableSources"/>, so the module stays live. Built-in stores read the
/// changed projections by storage id in one query per table. A row deleted before that lookup goes out with no
/// projection, so open views refetch it and say it no longer exists.
/// </para>
/// </remarks>
internal sealed class MessagingChangeSource(MessagingStores stores, int pageSize, TimeSpan overlapWindow)
{
    private const string _FormatVersion = "m1";
    private const char _StoreSeparator = '|';
    private const char _NameSeparator = '=';
    private const string _NotStarted = "-";

    private readonly MessagingStores _stores = Argument.IsNotNull(stores);

    public async Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        var answers = await _stores
            .ReadEachAsync(
                _stores.All,
                static async (store, token) =>
                    await store.Storage.GetMessageChangeStartCursorAsync(token).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        var cursors = new Dictionary<string, MessageChangeCursor?>(StringComparer.OrdinalIgnoreCase);

        foreach (var store in _stores.All)
        {
            // A store that did not answer starts on a later read, once it does.
            cursors[store.Name] = answers.Answered.FirstOrDefault(answer => ReferenceEquals(answer.Store, store)).Value;
        }

        return _Format(cursors);
    }

    /// <summary>Reads the changes after <paramref name="cursor"/> from every store, each read back for its projection.</summary>
    /// <exception cref="ArgumentException"><paramref name="cursor"/> is not a Messaging change cursor.</exception>
    public async Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        if (!_TryParse(cursor, out var parsed))
        {
            throw new ArgumentException($"'{cursor}' is not a Messaging change cursor.", nameof(cursor));
        }

        var answers = await _stores
            .ReadEachAsync(
                _stores.All,
                (store, token) => _ReadStoreAsync(store, parsed.GetValueOrDefault(store.Name), token),
                cancellationToken
            )
            .ConfigureAwait(false);

        var next = new Dictionary<string, MessageChangeCursor?>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<DashboardChange>();
        var hasMore = false;

        foreach (var store in _stores.All)
        {
            // A store that did not answer keeps the position it had, so its next read resumes where this one stopped.
            next[store.Name] = parsed.GetValueOrDefault(store.Name);
        }

        foreach (var (store, (storeChanges, storeCursor, storeHasMore)) in answers.Answered)
        {
            changes.AddRange(storeChanges);
            next[store.Name] = storeCursor;
            hasMore |= storeHasMore;
        }

        return new DashboardChangeBatch
        {
            Changes = changes,
            Cursor = _Format(next),
            UnavailableSources = answers.Unavailable,
            HasMore = hasMore,
        };
    }

    private async Task<(
        IReadOnlyList<DashboardChange> Changes,
        MessageChangeCursor Cursor,
        bool HasMore
    )> _ReadStoreAsync(MessagingStore store, MessageChangeCursor? cursor, CancellationToken cancellationToken)
    {
        if (cursor is null)
        {
            // A store that has no position yet starts now: what changed while it was unreachable shows on its lists.
            return (
                [],
                await store.Storage.GetMessageChangeStartCursorAsync(cancellationToken).ConfigureAwait(false),
                false
            );
        }

        var page = await store
            .Storage.GetMessageChangesAsync(cursor, overlapWindow, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (page.Changes.Count == 0)
        {
            return ([], page.Cursor, false);
        }

        var published = await _ReadRowsAsync(store, MessageChangeKind.Published, page.Changes, cancellationToken)
            .ConfigureAwait(false);
        var received = store.HoldsReceived
            ? await _ReadRowsAsync(store, MessageChangeKind.Received, page.Changes, cancellationToken)
                .ConfigureAwait(false)
            : [];

        var changes = new List<DashboardChange>(page.Changes.Count);

        foreach (var change in page.Changes)
        {
            var type = change.Kind == MessageChangeKind.Published ? MessageType.Publish : MessageType.Subscribe;
            var id = new MessagingObjectId(_stores.NodeName, store.Name, change.Id);
            var rows = type == MessageType.Publish ? published : received;
            var version = MessagingViews.VersionOf(change.ChangedAt);

            if (!rows.TryGetValue(change.Id, out var row))
            {
                changes.Add(MessagingViews.Removed(type, id, version));

                continue;
            }

            changes.Add(
                MessagingViews.Change(
                    type,
                    id,
                    row,
                    version,
                    // A change read cannot tell whether a row entered or left a Needs-you group, so a failure or a
                    // success refreshes it.
                    MessagingViews.AffectsAttention(change.Status)
                )
            );
        }

        // A full page may have stopped short of the store's latest change.
        return (changes, page.Cursor, page.Changes.Count >= pageSize);
    }

    private async Task<Dictionary<Guid, MessagingRowView>> _ReadRowsAsync(
        MessagingStore store,
        MessageChangeKind kind,
        IReadOnlyList<MessageChange> changes,
        CancellationToken cancellationToken
    )
    {
        var selected = changes.Where(change => change.Kind == kind).ToArray();

        if (selected.Length == 0)
        {
            return [];
        }

        Guid[] ids = [.. selected.Select(static change => change.Id).Distinct()];
        var type = kind == MessageChangeKind.Published ? MessageType.Publish : MessageType.Subscribe;
        var monitoring = store.Monitoring;
        if (monitoring is IStorageIdMonitoringApi exact)
        {
            var projections = await exact.GetMessageViewsAsync(type, ids, cancellationToken).ConfigureAwait(false);

            return projections.ToDictionary(
                static row => row.View.StorageId,
                row =>
                    MessagingRowView.From(
                        new MessagingObjectId(_stores.NodeName, store.Name, row.View.StorageId),
                        row.View
                    ) with
                    {
                        Reason = MessagingViews.ReasonOf(row.View.StatusName, row.ExceptionInfo),
                    }
            );
        }

        var details =
            type == MessageType.Publish
                ? await monitoring.GetPublishedMessagesAsync(ids, cancellationToken).ConfigureAwait(false)
                : await monitoring.GetReceivedMessagesAsync(ids, cancellationToken).ConfigureAwait(false);
        var byId = details.ToDictionary(static row => row.StorageId);
        var result = new Dictionary<Guid, MessagingRowView>(byId.Count);

        foreach (var change in selected)
        {
            if (result.ContainsKey(change.Id) || !byId.TryGetValue(change.Id, out var row))
            {
                continue;
            }

            // An older provider can still supply the detail even if the list projection disappears between reads.
            var id = new MessagingObjectId(_stores.NodeName, store.Name, change.Id);
            var view = await MessagingViews
                .ReadViewAsync(store, type, change.Id, MessagingViews.MessageIdOf(row), cancellationToken)
                .ConfigureAwait(false);
            result.Add(
                change.Id,
                view is null
                    ? MessagingRowView.From(id, type, row, change.Status, change.ChangedAt)
                    : MessagingRowView.From(id, view) with
                    {
                        Reason = MessagingViews.ReasonOf(view.StatusName, row.ExceptionInfo),
                    }
            );
        }

        return result;
    }

    private static string _Format(IReadOnlyDictionary<string, MessageChangeCursor?> cursors)
    {
        var builder = new StringBuilder(_FormatVersion);

        foreach (var (name, cursor) in cursors)
        {
            builder
                .Append(_StoreSeparator)
                .Append(name)
                .Append(_NameSeparator)
                .Append(cursor?.ToString() ?? _NotStarted);
        }

        return builder.ToString();
    }

    private static bool _TryParse(string? value, out Dictionary<string, MessageChangeCursor?> cursors)
    {
        cursors = new Dictionary<string, MessageChangeCursor?>(StringComparer.OrdinalIgnoreCase);
        var parts = value?.Split(_StoreSeparator);

        if (parts is not { Length: > 0 } || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var part in parts.AsSpan(1))
        {
            var separator = part.IndexOf(_NameSeparator, StringComparison.Ordinal);

            if (separator <= 0)
            {
                return false;
            }

            var name = part[..separator];
            var text = part[(separator + 1)..];

            if (string.Equals(text, _NotStarted, StringComparison.Ordinal))
            {
                cursors[name] = null;
            }
            else if (MessageChangeCursor.TryParse(text, out var cursor))
            {
                cursors[name] = cursor;
            }
            else
            {
                return false;
            }
        }

        return true;
    }
}
