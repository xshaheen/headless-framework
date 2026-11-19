// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.FileLocks;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Headless.Tus.Internal;

/// <summary>
/// Finds finished uploads for the application, with the same owner check its upload endpoints apply.
/// </summary>
internal sealed class TusUploads(
    ITusStore store,
    TusEndpointRegistry endpoints,
    TusUploadBinding binding,
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider services,
    ITusFileLockProvider? fileLockProvider = null
) : ITusUploads
{
    /// <summary>The lock tusdotnet itself takes around a request that writes to an upload or deletes it.</summary>
    private readonly ITusFileLockProvider _fileLockProvider = fileLockProvider ?? InMemoryFileLockProvider.Instance;

    private ICurrentTenant? _currentTenant;

    public Task<TusUpload?> FindAsync(string fileId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(fileId);

        return _FindForCallerAsync(fileId, Options.DefaultName, cancellationToken);
    }

    public Task<TusUpload?> FindAsync(string fileId, string endpointName, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(fileId);
        Argument.IsNotNullOrWhiteSpace(endpointName);

        return _FindForCallerAsync(fileId, endpointName, cancellationToken);
    }

    public Task<TusUpload?> FindUncheckedAsync(string fileId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(fileId);

        if (!binding.IsScopedByTenant)
        {
            return _FindFinishedAsync(fileId, static _ => true, cancellationToken);
        }

        // Resolved on first use, and only once uploads are scoped: the tenancy setup that turns scoping on
        // registers a current tenant, and a host that never scopes them may have none.
        var tenantId = (_currentTenant ??= services.GetRequiredService<ICurrentTenant>()).Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new MissingTenantContextException(
                "Uploads are scoped by tenant, and an upload was looked up with no ambient tenant. Wrap the call in "
                    + "ICurrentTenant.Change(tenantId) with the tenant the upload belongs to."
            );
        }

        // A store that keeps uploads per tenant already looks nowhere else. Any other store holds every tenant's
        // uploads side by side, so the stamp decides.
        return _FindFinishedAsync(
            fileId,
            stamp => string.Equals(stamp.TenantId, tenantId, StringComparison.Ordinal),
            cancellationToken
        );
    }

    private async Task<TusUpload?> _FindForCallerAsync(
        string fileId,
        string endpointName,
        CancellationToken cancellationToken
    )
    {
        if (!endpoints.TryGet(endpointName, out var endpoint))
        {
            throw new InvalidOperationException(
                endpointName.Length == 0
                    ? "The default upload endpoint is not declared. Call tus.AddUploadEndpoint(options => …) inside "
                        + "AddHeadlessTus, or pass the name of a declared endpoint."
                    : $"The upload endpoint \"{endpointName}\" is not declared. Call "
                        + $"tus.AddUploadEndpoint(\"{endpointName}\", …) inside AddHeadlessTus."
            );
        }

        var httpContext =
            httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                $"{nameof(ITusUploads)}.{nameof(ITusUploads.FindAsync)} compares an upload with the caller of the "
                    + "current HTTP request, and there is no current request. Outside a request, call "
                    + $"{nameof(ITusUploads.FindUncheckedAsync)} and compare the upload's owner in the application."
            );

        var caller = await TusUploadBinding.ResolveCallerAsync(httpContext, endpoint).ConfigureAwait(false);

        if (caller is null || !binding.CanHaveUploads(caller))
        {
            return null;
        }

        return await _FindFinishedAsync(fileId, stamp => stamp == caller, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the upload when it carries a stamp <paramref name="isWanted"/> accepts and holds every byte of a
    /// declared length.
    /// </summary>
    private async Task<TusUpload?> _FindFinishedAsync(
        string fileId,
        Func<TusUploadStamp, bool> isWanted,
        CancellationToken cancellationToken
    )
    {
        var rawMetadata = await binding.ReadMetadataAsync(fileId, cancellationToken).ConfigureAwait(false);
        var stamp = TusUploadStamp.Read(rawMetadata);

        if (stamp is null || !isWanted(stamp))
        {
            return null;
        }

        var length = await _GetFinishedLengthAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (length is null)
        {
            return null;
        }

        var metadata = MetadataParser
            .ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, TusUploadStamp.Strip(rawMetadata))
            .Metadata;

        return new TusUpload(
            contentToken => _OpenContentAsync(fileId, contentToken),
            deleteToken => _DeleteAsync(fileId, deleteToken)
        )
        {
            FileId = fileId,
            Length = length.Value,
            Metadata = metadata.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.GetString(Encoding.UTF8),
                StringComparer.Ordinal
            ),
            EndpointName = stamp.Endpoint,
            TenantId = stamp.TenantId,
            OwnerKey = stamp.OwnerKey,
        };
    }

    /// <summary>
    /// The upload's length when it is finished, read under the upload's file lock so the application is never
    /// given an upload whose client could still rewrite or delete it. <see langword="null"/> when the upload is not
    /// finished, and while another request holds the lock.
    /// </summary>
    private async Task<long?> _GetFinishedLengthAsync(string fileId, CancellationToken cancellationToken)
    {
        var fileLock = await _fileLockProvider.AquireLock(fileId).ConfigureAwait(false);

        if (!await fileLock.Lock().ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return await binding.ReadFinishedLengthAsync(fileId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await fileLock.ReleaseIfHeld().ConfigureAwait(false);
        }
    }

    private async Task<Stream> _OpenContentAsync(string fileId, CancellationToken cancellationToken)
    {
        if (store is not ITusReadableStore readableStore)
        {
            throw new NotSupportedException(
                $"The upload store {store.GetType().Name} does not implement {nameof(ITusReadableStore)}, so an "
                    + "upload's content cannot be opened through it."
            );
        }

        var file =
            await readableStore.GetFileAsync(fileId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The upload {fileId} is no longer in the store.");

        return await file.GetContentAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task _DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        if (store is not ITusTerminationStore terminationStore)
        {
            throw new NotSupportedException(
                $"The upload store {store.GetType().Name} does not implement {nameof(ITusTerminationStore)}, so "
                    + "an upload cannot be deleted through it."
            );
        }

        return terminationStore.DeleteFileAsync(fileId, cancellationToken);
    }
}
