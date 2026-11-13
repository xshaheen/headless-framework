// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Headless.Tus.Internal;

/// <summary>
/// Removes an upload the endpoint itself decided to remove: one whose content it refused, or one the
/// application's completion handler asked to have deleted or refused.
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

    /// <summary>
    /// Removes the upload once the response to <paramref name="httpContext"/> is sent, for a request whose answer
    /// still needs the upload: tusdotnet reads an upload's offset to build the response to the request that
    /// created it or asked for its status.
    /// </summary>
    /// <param name="httpContext">The request being answered.</param>
    /// <param name="fileId">The upload to remove.</param>
    /// <param name="tenantId">The tenant the request runs in, or <see langword="null"/> for none.</param>
    public void RemoveAfterResponse(HttpContext httpContext, string fileId, string? tenantId)
    {
        var currentTenant = httpContext.RequestServices.GetService<ICurrentTenant>();

        httpContext.Response.OnCompleted(async () =>
        {
            // The application's tenant scope has ended by now, and a store that keeps uploads per tenant needs
            // the tenant the upload was created in.
            using (currentTenant?.Change(tenantId))
            {
                await RemoveAsync(fileId).ConfigureAwait(false);
            }
        });
    }

    [LoggerMessage(
        EventId = 3305,
        Level = LogLevel.Error,
        Message = "Could not delete upload {FileId}, which the upload endpoint decided to remove; it stays in the store"
    )]
    private static partial void LogUploadNotRemoved(ILogger logger, Exception exception, string fileId);
}
