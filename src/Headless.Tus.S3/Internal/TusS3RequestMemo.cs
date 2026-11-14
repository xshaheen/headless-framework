// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Tus.Internal;

/// <summary>
/// Keeps each upload's loaded state, and the ETag it was read or written with, for the rest of the current HTTP
/// request.
/// </summary>
/// <remarks>
/// tusdotnet calls several store methods per HEAD and before each append, and each would otherwise read the state
/// again. Every successful write replaces the entry, so the writes of one request chain their ETags instead of failing
/// on their own earlier write. A failed write removes the entry, because the in-memory state was changed for a write
/// that did not land. Outside an HTTP request, or without an accessor, nothing is kept and every call reads storage.
/// </remarks>
internal sealed class TusS3RequestMemo(IHttpContextAccessor? httpContextAccessor)
{
    // One key per memo instance, so two stores sharing a request never see each other's entries.
    private readonly object _itemsKey = new();

    public bool TryGet(string? tenant, string fileId, [NotNullWhen(true)] out TusS3LoadedUpload? upload)
    {
        upload = null;

        return _GetEntries(create: false)?.TryGetValue((tenant, fileId), out upload) == true;
    }

    public void Set(string? tenant, string fileId, TusS3LoadedUpload upload)
    {
        _GetEntries(create: true)?[(tenant, fileId)] = upload;
    }

    public void Remove(string? tenant, string fileId)
    {
        _GetEntries(create: false)?.Remove((tenant, fileId));
    }

    private Dictionary<(string? Tenant, string FileId), TusS3LoadedUpload>? _GetEntries(bool create)
    {
        var items = httpContextAccessor?.HttpContext?.Items;

        if (items is null)
        {
            return null;
        }

        if (
            items.TryGetValue(_itemsKey, out var existing)
            && existing is Dictionary<(string? Tenant, string FileId), TusS3LoadedUpload> found
        )
        {
            return found;
        }

        if (!create)
        {
            return null;
        }

        // Keyed by tenant as well as file id: code that changes the ambient tenant inside one request must never
        // be handed the upload another tenant holds under the same id.
        var entries = new Dictionary<(string? Tenant, string FileId), TusS3LoadedUpload>();
        items[_itemsKey] = entries;

        return entries;
    }
}
