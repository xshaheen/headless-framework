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
/// in <see cref="DashboardChangeBatch.UnavailableSources"/>, so the module stays live. Each changed row is read back
/// for its projection: by id in one read per store and table, then its list row. A row deleted between the change
/// read and its read-back goes out with no projection, so open views refetch it and say it no longer exists.
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

        foreach (var store in _stores.All)
        {
            // A store that did not answer keeps the position it had, so its next read resumes where this one stopped.
            next[store.Name] = parsed.GetValueOrDefault(store.Name);
        }

        foreach (var (store, (storeChanges, storeCursor)) in answers.Answered)
        {
            changes.AddRange(storeChanges);
            next[store.Name] = storeCursor;
        }

        return new DashboardChangeBatch
        {
            Changes = changes,
            Cursor = _Format(next),
            UnavailableSources = answers.Unavailable,
        };
    }

    private async Task<(IReadOnlyList<DashboardChange> Changes, MessageChangeCursor Cursor)> _ReadStoreAsync(
        MessagingStore store,
        MessageChangeCursor? cursor,
        CancellationToken cancellationToken
    )
    {
        if (cursor is null)
        {
            // A store that has no position yet starts now: what changed while it was unreachable shows on its lists.
            return ([], await store.Storage.GetMessageChangeStartCursorAsync(cancellationToken).ConfigureAwait(false));
        }

        var page = await store
            .Storage.GetMessageChangesAsync(cursor, overlapWindow, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (page.Changes.Count == 0)
        {
            return ([], page.Cursor);
        }

        var published = await _ReadBackAsync(store, MessageChangeKind.Published, page.Changes, cancellationToken)
            .ConfigureAwait(false);
        var received = store.HoldsReceived
            ? await _ReadBackAsync(store, MessageChangeKind.Received, page.Changes, cancellationToken)
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

            // The by-id read lacks what a list row shows, such as a received row's consumer and hold, so the list row
            // is read too; a row whose list row is gone by now still goes out with what the by-id read had.
            var view = await MessagingViews
                .ReadViewAsync(store, type, change.Id, MessagingViews.MessageIdOf(row), cancellationToken)
                .ConfigureAwait(false);

            changes.Add(
                MessagingViews.Change(
                    type,
                    id,
                    view is null
                        ? MessagingRowView.From(id, type, row, change.Status, change.ChangedAt)
                        : MessagingRowView.From(id, view),
                    version,
                    // A change read cannot tell whether a row entered or left a Needs-you group, so a failure or a
                    // success refreshes it.
                    MessagingViews.AffectsAttention(change.Status)
                )
            );
        }

        return (changes, page.Cursor);
    }

    private static async Task<Dictionary<Guid, MediumMessage>> _ReadBackAsync(
        MessagingStore store,
        MessageChangeKind kind,
        IReadOnlyList<MessageChange> changes,
        CancellationToken cancellationToken
    )
    {
        Guid[] ids = [.. changes.Where(change => change.Kind == kind).Select(static change => change.Id).Distinct()];

        if (ids.Length == 0)
        {
            return [];
        }

        var rows =
            kind == MessageChangeKind.Published
                ? await store.Monitoring.GetPublishedMessagesAsync(ids, cancellationToken).ConfigureAwait(false)
                : await store.Monitoring.GetReceivedMessagesAsync(ids, cancellationToken).ConfigureAwait(false);

        return rows.ToDictionary(static row => row.StorageId);
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
