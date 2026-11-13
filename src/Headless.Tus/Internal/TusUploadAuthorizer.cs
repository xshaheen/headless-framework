// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace Headless.Tus.Internal;

/// <summary>
/// Decides, before tusdotnet touches the store, whether a request to an upload endpoint may go on: a request that
/// creates an upload needs an owner, and a request that names an upload must come from the endpoint, owner, and
/// tenant the upload was stamped with.
/// </summary>
/// <remarks>
/// It runs as tusdotnet's authorization event, which tusdotnet calls outside its own error handling: an exception
/// that leaves <see cref="AuthorizeAsync"/> becomes a 500, so every outcome is a refusal set on the context.
/// </remarks>
internal sealed partial class TusUploadAuthorizer(
    ITusStore store,
    IOptions<TusTenantScopingOptions> tenantScoping,
    ILogger<TusUploadAuthorizer> logger
)
{
    /// <summary>Lets the request go on, or sets the refusal tusdotnet answers it with.</summary>
    /// <param name="context">tusdotnet's authorization context for the request.</param>
    /// <param name="caller">
    /// The stamp an upload created by this request gets, or <see langword="null"/> when the request has no owner.
    /// </param>
    public async Task AuthorizeAsync(AuthorizeContext context, TusUploadStamp? caller)
    {
        // The intent, never the HTTP method: a client may send any intent as a POST that overrides its method.
        if (context.Intent == IntentType.GetOptions)
        {
            return;
        }

        if (caller is null)
        {
            context.FailRequest(HttpStatusCode.Unauthorized, "The request has no upload owner.");

            return;
        }

        if (context.Intent == IntentType.CreateFile)
        {
            _AuthorizeCreate(context, caller);

            return;
        }

        TusUploadStamp? stamp;

        try
        {
            stamp = await _ReadStampAsync(context.FileId, context.CancellationToken).ConfigureAwait(false);
        }
        catch (TusStoreException)
        {
            // A store's own refusal of the id, for example one it could not have issued: no such upload.
            stamp = null;
        }
        catch (Exception exception)
        {
            if (!context.CancellationToken.IsCancellationRequested)
            {
                LogStampReadFailed(logger, exception, context.FileId);
            }

            // Not "not found": the upload may well be the caller's, and a client told that it is gone gives it up.
            // A 503 on a write is a failure clients retry.
            context.FailRequest(HttpStatusCode.ServiceUnavailable, "The upload store is not available.");

            return;
        }

        if (stamp != caller)
        {
            _FailAsNotFound(context);
        }
    }

    private void _AuthorizeCreate(AuthorizeContext context, TusUploadStamp caller)
    {
        if (caller.OwnerKey.Length > TusUploadStamp.MaxOwnerKeyLength)
        {
            context.FailRequest(
                HttpStatusCode.BadRequest,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The upload owner key is longer than {TusUploadStamp.MaxOwnerKeyLength} characters."
                )
            );

            return;
        }

        if (caller.TenantId is null && tenantScoping.Value.Enabled)
        {
            context.FailRequest(HttpStatusCode.Forbidden, "The request has no tenant.");
        }
    }

    /// <summary>
    /// Reads the upload's stamp from the host's store, in one read that also tells whether the upload exists.
    /// </summary>
    private async Task<TusUploadStamp?> _ReadStampAsync(string? fileId, CancellationToken cancellationToken)
    {
        // Only a creation store keeps metadata, and without one no upload was ever stamped.
        if (string.IsNullOrEmpty(fileId) || store is not ITusCreationStore creationStore)
        {
            return null;
        }

        var metadata = await creationStore.GetUploadMetadataAsync(fileId, cancellationToken).ConfigureAwait(false);

        return TusUploadStamp.Read(metadata);
    }

    /// <summary>
    /// Refuses the request with the response tusdotnet itself gives for an upload that does not exist, so the
    /// answer tells a caller nothing about an upload that is not theirs.
    /// </summary>
    private static void _FailAsNotFound(AuthorizeContext context)
    {
        // tusdotnet puts these two headers on its own 404 and leaves them off a failed authorization.
        var headers = context.HttpContext.Response.Headers;
        headers["Tus-Resumable"] = "1.0.0";
        headers[HeaderNames.CacheControl] = "no-store";

        context.FailRequest(HttpStatusCode.NotFound);
    }

    [LoggerMessage(
        EventId = 3285,
        Level = LogLevel.Error,
        Message = "Could not read upload {FileId} from the store to authorize the request; answered 503"
    )]
    private static partial void LogStampReadFailed(ILogger logger, Exception exception, string? fileId);
}
