// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Tus.Internal;

/// <summary>
/// Keeps what a store loaded about each upload for the rest of the current HTTP request.
/// </summary>
/// <typeparam name="TEntry">the store's view of one upload, including the version its next write is conditional on</typeparam>
/// <remarks>
/// tusdotnet calls several store methods per HEAD and PATCH, and each would otherwise read storage again. A store sets
/// the entry after every successful write, so the writes of one request chain their versions instead of failing on
/// their own earlier write, and removes it after a failed write, because the in-memory view was changed for a write
/// that did not land. Outside an HTTP request, or without an accessor, nothing is kept and every call reads storage.
/// <para>
/// The key names one upload of one tenant. A file id alone is not enough: code that changes the ambient tenant
/// inside one request must never be handed the upload another tenant holds under the same id, so a store that
/// keeps uploads per tenant puts the tenant into the key.
/// </para>
/// </remarks>
internal sealed class TusRequestMemo<TEntry>(IHttpContextAccessor? httpContextAccessor)
    where TEntry : class
{
    // One key per memo instance, so two stores sharing a request never see each other's entries.
    private readonly object _itemsKey = new();

    public bool TryGet(string key, [NotNullWhen(true)] out TEntry? entry)
    {
        entry = null;

        return _GetEntries(create: false)?.TryGetValue(key, out entry) == true;
    }

    public void Set(string key, TEntry entry)
    {
        _GetEntries(create: true)?[key] = entry;
    }

    public void Remove(string key)
    {
        _GetEntries(create: false)?.Remove(key);
    }

    private Dictionary<string, TEntry>? _GetEntries(bool create)
    {
        var items = httpContextAccessor?.HttpContext?.Items;

        if (items is null)
        {
            return null;
        }

        if (items.TryGetValue(_itemsKey, out var existing) && existing is Dictionary<string, TEntry> found)
        {
            return found;
        }

        if (!create)
        {
            return null;
        }

        var entries = new Dictionary<string, TEntry>(StringComparer.Ordinal);
        items[_itemsKey] = entries;

        return entries;
    }
}
