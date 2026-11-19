// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Headless.Tus.Internal;

/// <summary>
/// Removes an upload the endpoint itself decided to remove: one whose content it refused.
/// </summary>
/// <remarks>
/// A removal is never tied to the request: the decision stands whether or not the client is still there, so a
/// client that disconnects must not keep the upload alive. It never throws either, because the answer to the
/// client was decided before it; a failure is logged, and the upload stays in the store.
/// </remarks>
internal sealed partial class TusUploadRemover(ITusStore store, ILogger<TusUploadRemover> logger)
{
    /// <summary>Removes the upload now.</summary>
    public async ValueTask RemoveAsync(string fileId)
    {
        // Without the termination extension the store cannot delete, and the upload stays where it is.
        if (store is not ITusTerminationStore terminationStore)
        {
            return;
        }

        try
        {
            await terminationStore.DeleteFileAsync(fileId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogUploadNotRemoved(logger, exception, fileId);
        }
    }

    [LoggerMessage(
        EventId = 3305,
        Level = LogLevel.Error,
        Message = "Could not delete upload {FileId}, which the upload endpoint decided to remove; it stays in the store"
    )]
    private static partial void LogUploadNotRemoved(ILogger logger, Exception exception, string fileId);
}
