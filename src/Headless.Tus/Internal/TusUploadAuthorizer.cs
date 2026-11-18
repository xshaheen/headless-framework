// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
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
internal sealed partial class TusUploadAuthorizer(TusUploadBinding binding, ILogger<TusUploadAuthorizer> logger)
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
            await _RefuseWithoutOwnerAsync(context).ConfigureAwait(false);

            return;
        }

        if (context.Intent == IntentType.CreateFile)
        {
            _AuthorizeCreate(context, caller);

            return;
        }

        if (!binding.CanHaveUploads(caller))
        {
            _FailAsNotFound(context);

            return;
        }

        TusUploadStamp? stamp;

        try
        {
            stamp = TusUploadStamp.Read(
                await binding.ReadMetadataAsync(context.FileId, context.CancellationToken).ConfigureAwait(false)
            );
        }
        catch (Exception exception)
        {
            if (!context.CancellationToken.IsCancellationRequested)
            {
                LogStampReadFailed(logger, exception, context.FileId);
            }

            // Not "not found": the upload may well be the caller's, and a client told that it is gone gives it up.
            // A 503 on a write is a failure clients retry. A status request is different: a tus client asks the
            // same upload again only after 423, and takes any other failure as the loss of the upload, which it
            // then sends again from its first byte.
            if (context.Intent == IntentType.GetFileInfo)
            {
                TusProtocol.Fail(context, HttpStatusCode.Locked, "The upload store is not available.");
            }
            else
            {
                TusProtocol.Fail(context, HttpStatusCode.ServiceUnavailable, "The upload store is not available.");
            }

            return;
        }

        if (stamp != caller)
        {
            _FailAsNotFound(context);
        }
    }

    /// <summary>
    /// Refuses a client's request to delete an upload that holds every byte of its declared length. Such an upload
    /// is the application's: it may already have been given the upload, or keep it where it is, and nothing on
    /// the upload says which. An upload that still lacks bytes, or a declared length, stays its client's to cancel.
    /// </summary>
    /// <remarks>
    /// It runs as tusdotnet's before-delete event, under the upload's file lock, so the write that stores the
    /// upload's last byte cannot be in flight while it decides.
    /// </remarks>
    public async Task AuthorizeDeleteAsync(BeforeDeleteContext context)
    {
        var finishedLength = await binding
            .ReadFinishedLengthAsync(context.FileId, context.CancellationToken)
            .ConfigureAwait(false);

        if (finishedLength is not null)
        {
            context.FailRequest(
                HttpStatusCode.Forbidden,
                "The upload is complete, so it can no longer be deleted through this endpoint."
            );
        }
    }

    private void _AuthorizeCreate(AuthorizeContext context, TusUploadStamp caller)
    {
        if (caller.OwnerKey.Length > TusUploadStamp.MaxOwnerKeyLength)
        {
            TusProtocol.Fail(
                context,
                HttpStatusCode.BadRequest,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The upload owner key is longer than {TusUploadStamp.MaxOwnerKeyLength} characters."
                )
            );

            return;
        }

        if (!binding.IsScopedByTenant)
        {
            return;
        }

        if (caller.TenantId is null)
        {
            TusProtocol.Fail(context, HttpStatusCode.Forbidden, "The request has no tenant.");

            return;
        }

        // A store that keeps uploads per tenant would refuse the id as a path segment after tusdotnet has already
        // run the create checks, and that refusal would surface as a server error.
        if (!TenantStorageSegment.IsValid(caller.TenantId))
        {
            TusProtocol.Fail(
                context,
                HttpStatusCode.Forbidden,
                "The request's tenant id cannot be used to store uploads."
            );
        }
    }

    /// <summary>
    /// Refuses a request that has no owner. A caller who is not signed in gets the host's own authentication
    /// challenge, so the response says how to sign in, as it would behind an authorization policy. A caller who is
    /// signed in gets 403, because signing in again would not give the request an owner.
    /// </summary>
    private static async Task _RefuseWithoutOwnerAsync(AuthorizeContext context)
    {
        const string message = "The request has no upload owner.";
        var httpContext = context.HttpContext;

        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            TusProtocol.Fail(context, HttpStatusCode.Forbidden, message);

            return;
        }

        var schemes = httpContext.RequestServices.GetService<IAuthenticationSchemeProvider>();

        if (schemes is null || await schemes.GetDefaultChallengeSchemeAsync().ConfigureAwait(false) is null)
        {
            TusProtocol.Fail(context, HttpStatusCode.Unauthorized, message);

            return;
        }

        // The scheme sets the status and its headers, such as WWW-Authenticate for a bearer token or a redirect to
        // the sign-in page for a cookie; tusdotnet then writes the response with that status.
        await httpContext.ChallengeAsync().ConfigureAwait(false);

        var challenged = (HttpStatusCode)httpContext.Response.StatusCode;

        if (challenged == HttpStatusCode.Unauthorized || (int)challenged is < 300 or >= 500)
        {
            TusProtocol.Fail(context, HttpStatusCode.Unauthorized, message);
        }
        else
        {
            TusProtocol.Fail(context, challenged);
        }
    }

    /// <summary>
    /// Refuses the request with the response tusdotnet itself gives for an upload that does not exist, so the
    /// answer tells a caller nothing about an upload that is not theirs.
    /// </summary>
    private static void _FailAsNotFound(AuthorizeContext context)
    {
        // tusdotnet's own 404 carries no-store whatever the request, and TusProtocol adds it only to a status
        // request.
        context.HttpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
        TusProtocol.Fail(context, HttpStatusCode.NotFound);
    }

    [LoggerMessage(
        EventId = 3285,
        Level = LogLevel.Error,
        Message = "Could not read upload {FileId} from the store to authorize the request; answered 503, or 423 to a status request"
    )]
    private static partial void LogStampReadFailed(ILogger logger, Exception exception, string? fileId);
}
