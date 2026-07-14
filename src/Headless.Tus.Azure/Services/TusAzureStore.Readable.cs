// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus.Models;
using tusdotnet.Interfaces;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Tus;

public sealed partial class TusAzureStore : ITusReadableStore
{
    /// <summary>
    /// Returns an <c>ITusFile</c> for the given upload that provides access to its content
    /// stream and metadata, or <see langword="null"/> if no such file exists.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="cancellationToken">token to cancel the operation</param>
    /// <returns>
    /// an <c>ITusFile</c> wrapping the blob, or <see langword="null"/> if the blob is not found
    /// </returns>
    public async Task<ITusFile?> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        var blobClient = _GetBlobClient(fileId);
        var tusFile = await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken).ConfigureAwait(false);

        return tusFile == null ? null : new TusAzureFileWrapper(tusFile, blobClient, _logger);
    }
}
