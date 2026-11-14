// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Headless.Tus.Internal;

/// <summary>
/// Answers the two questions every check of an upload's owner starts from: who the caller of a request is, and
/// what the upload was stamped with. An upload endpoint and <see cref="ITusUploads"/> both ask here, so an
/// application's own lookup of an upload cannot answer differently from the endpoint that serves it.
/// </summary>
internal sealed class TusUploadBinding(ITusStore store, IOptions<TusTenantScopingOptions> tenantScoping)
{
    /// <summary>Whether every upload belongs to a tenant and is stored under that tenant's location.</summary>
    public bool IsScopedByTenant => tenantScoping.Value.Enabled;

    /// <summary>
    /// Returns the stamp an upload created by this request gets, which is also what an upload the request names
    /// must carry, or <see langword="null"/> when the request has no owner.
    /// </summary>
    public static async ValueTask<TusUploadStamp?> ResolveCallerAsync(
        HttpContext context,
        TusEndpointRegistration endpoint
    )
    {
        // Resolved from the request's services: an application may register either one per request.
        var ownerKey = endpoint.OwnerKey is { } ownedBy
            ? await ownedBy(context).ConfigureAwait(false)
            : context.RequestServices.GetService<ICurrentUser>()?.UserId?.GetUnderlyingPrimitiveValue();

        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return null;
        }

        var tenantId = context.RequestServices.GetService<ICurrentTenant>()?.Id;

        return new TusUploadStamp(endpoint.Name, ownerKey, string.IsNullOrEmpty(tenantId) ? null : tenantId);
    }

    /// <summary>
    /// Whether <paramref name="caller"/> can have an upload in the store at all. With uploads scoped by tenant
    /// every upload belongs to a tenant, so a caller with none, or with one no upload can be stored under, has no
    /// upload to find. The store must not be asked for such a caller: a store that keeps uploads per tenant
    /// refuses to look outside one, and that refusal would read as a store failure.
    /// </summary>
    public bool CanHaveUploads(TusUploadStamp caller)
    {
        return !IsScopedByTenant || TenantStorageSegment.IsValid(caller.TenantId);
    }

    /// <summary>
    /// Reads the upload's metadata as the store keeps it, stamp included, in one read that also tells whether the
    /// upload exists.
    /// </summary>
    /// <returns>
    /// The stored metadata, or <see langword="null"/> when the upload does not exist, the id is one the store
    /// could not have issued, or the store keeps no metadata.
    /// </returns>
    public async Task<string?> ReadMetadataAsync(string? fileId, CancellationToken cancellationToken)
    {
        // Only a creation store keeps metadata, and without one no upload was ever stamped.
        if (string.IsNullOrEmpty(fileId) || store is not ITusCreationStore creationStore)
        {
            return null;
        }

        try
        {
            return await creationStore.GetUploadMetadataAsync(fileId, cancellationToken).ConfigureAwait(false);
        }
        catch (TusStoreException)
        {
            // A store's own refusal of the id, for example one it could not have issued: no such upload.
            return null;
        }
    }
}
