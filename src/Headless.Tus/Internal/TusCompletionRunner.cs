// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tusdotnet.FileLocks;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Parsers;

namespace Headless.Tus.Internal;

/// <summary>
/// Hands a finished upload to the endpoint's <see cref="ITusUploadCompletedHandler"/> and acts on what it
/// decides. On a store that records a handoff it also runs the handler for an upload that still owes one, when
/// the client asks for the upload's status, and keeps a client from deleting an upload the application took over.
/// </summary>
/// <remarks>
/// tusdotnet raises its completion event once, inside the request that stored the last byte, and never again. An
/// upload whose handler threw there, or whose request died before the event, would be lost to the application
/// while the client reports success, so the handoff is recorded on the upload only after the handler returns, and
/// a status request, which a tus client sends after any failed write, finds what is still owed.
/// </remarks>
internal sealed partial class TusCompletionRunner(
    ITusStore store,
    TusUploadRemover remover,
    ILogger<TusCompletionRunner> logger,
    ITusFileLockProvider? fileLockProvider = null
)
{
    /// <summary>
    /// The lock tusdotnet itself takes around a request for the upload, so a handler run here excludes the
    /// requests that write or delete the upload, and another run.
    /// </summary>
    private readonly ITusFileLockProvider _fileLockProvider = fileLockProvider ?? InMemoryFileLockProvider.Instance;

    /// <summary>
    /// Warns when <paramref name="endpoint"/> has a completion handler and the host's store cannot record a
    /// handoff, because a failed handler is then never run again and nothing else would say so.
    /// </summary>
    public void WarnWhenTheHandlerRunsOnce(TusEndpointRegistration endpoint)
    {
        if (endpoint.CompletedHandler is not null && store is not ITusHandoffStore)
        {
            LogHandlerRunsOnce(logger, endpoint.DisplayName, store.GetType().Name);
        }
    }

    /// <summary>
    /// Runs the endpoint's handler for the upload tusdotnet just completed. It runs as tusdotnet's completion
    /// event, under the upload's file lock.
    /// </summary>
    /// <param name="context">tusdotnet's completion context for the request.</param>
    /// <param name="handlerType">The endpoint's handler type.</param>
    /// <param name="caller">The stamp of the request, which is the upload's.</param>
    /// <param name="createdByThisRequest">Whether the upload was created by this same request.</param>
    /// <exception cref="TusUploadRejectedException">The handler refused the upload, which is removed.</exception>
    public async Task CompleteAsync(
        FileCompleteContext context,
        Type handlerType,
        TusUploadStamp caller,
        bool createdByThisRequest
    )
    {
        TusUploadHandoff? handoff = null;

        try
        {
            handoff = await _DecideAsync(
                    context.HttpContext,
                    context.FileId,
                    handlerType,
                    caller,
                    context.CancellationToken
                )
                .ConfigureAwait(false);

            await _ApplyAsync(context.HttpContext, context.FileId, caller, handoff).ConfigureAwait(false);
        }
        catch (Exception) when (createdByThisRequest && !ReferenceEquals(handoff, TusUploadHandoff.Keep))
        {
            // The client never receives the URL of an upload whose creating request fails, so no later request
            // could run the handler for it: the upload would stay in the store with nobody to take or delete it.
            // An upload the handler kept is the exception. The application already holds it, so a handoff that
            // could not be recorded fails the request and leaves the upload where the application expects it.
            await remover.RemoveAsync(context.FileId).ConfigureAwait(false);

            throw;
        }

        if (handoff.IsRejected)
        {
            // tusdotnet's completion event cannot fail its request, so the refusal leaves it as an exception
            // that the endpoint's request delegate answers.
            throw new TusUploadRejectedException(handoff.Message);
        }
    }

    /// <summary>
    /// Runs the endpoint's handler for a complete upload that still owes one, before tusdotnet answers a status
    /// request for it. It runs as part of tusdotnet's authorization event, after the caller was matched to the
    /// upload and before tusdotnet takes the upload's file lock.
    /// </summary>
    /// <remarks>
    /// Every failure is answered with 423: a tus client asks the same upload again only after that status, and
    /// takes any other failed status request as the loss of the upload, which it then sends again from its first
    /// byte. The one refusal with another status is the handler's own, which removes the upload.
    /// </remarks>
    /// <param name="context">tusdotnet's authorization context for the status request.</param>
    /// <param name="handlerType">The endpoint's handler type.</param>
    /// <param name="caller">The stamp of the request, which is the upload's.</param>
    public async Task RunOwedHandlerAsync(AuthorizeContext context, Type handlerType, TusUploadStamp caller)
    {
        if (store is not ITusHandoffStore handoffStore)
        {
            return;
        }

        var fileId = context.FileId;

        try
        {
            // What the request already read about the upload is enough to skip one that still lacks bytes
            // without taking its lock.
            if (!await _HasAllBytesAsync(fileId, context.CancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var fileLock = await _fileLockProvider.AquireLock(fileId).ConfigureAwait(false);

            if (!await fileLock.Lock().ConfigureAwait(false))
            {
                // Another request holds the upload, and it may be the one running the handler.
                TusProtocol.Fail(
                    context,
                    HttpStatusCode.Locked,
                    "The upload is being completed. Please try again later."
                );

                return;
            }

            string? refusal = null;

            try
            {
                // Read again under the lock, and from storage: the lock's last holder may have handed the upload
                // off or deleted it after this request first read it.
                var state = await handoffStore
                    .GetHandoffStateAsync(fileId, context.CancellationToken)
                    .ConfigureAwait(false);

                if (state == TusHandoffState.Pending)
                {
                    var handoff = await _DecideAsync(
                            context.HttpContext,
                            fileId,
                            handlerType,
                            caller,
                            context.CancellationToken
                        )
                        .ConfigureAwait(false);

                    await _ApplyAsync(context.HttpContext, fileId, caller, handoff).ConfigureAwait(false);

                    refusal = handoff.Message;
                }
            }
            finally
            {
                // tusdotnet takes this same lock for the status request next and would find it busy.
                await fileLock.ReleaseIfHeld().ConfigureAwait(false);
            }

            if (refusal is not null)
            {
                TusProtocol.Fail(context, HttpStatusCode.BadRequest, refusal);
            }
        }
        catch (Exception exception)
        {
            if (!context.CancellationToken.IsCancellationRequested)
            {
                LogOwedHandlerFailed(logger, exception, fileId);
            }

            TusProtocol.Fail(
                context,
                HttpStatusCode.Locked,
                "The upload could not be completed. Please try again later."
            );
        }
    }

    /// <summary>
    /// Refuses a client's request to delete an upload the application took over. It runs as tusdotnet's
    /// before-delete event, under the upload's file lock, so a handler cannot be handing the upload off at the
    /// same time.
    /// </summary>
    /// <remarks>
    /// A complete upload that still owes its handler is deleted without running it: the application is not handed
    /// a file its uploader is cancelling. A store that cannot record a handoff cannot tell the two apart, and
    /// there a complete upload counts as taken over, since its handler ran when it completed.
    /// </remarks>
    public async Task AuthorizeDeleteAsync(BeforeDeleteContext context)
    {
        var handedOff = store is ITusHandoffStore handoffStore
            ? await handoffStore.GetHandoffStateAsync(context.FileId, context.CancellationToken).ConfigureAwait(false)
                == TusHandoffState.HandedOff
            : await _HasAllBytesAsync(context.FileId, context.CancellationToken).ConfigureAwait(false);

        if (handedOff)
        {
            context.FailRequest(
                HttpStatusCode.Forbidden,
                "The upload is complete and was handed to the application, so it can no longer be deleted."
            );
        }
    }

    /// <summary>
    /// Wraps an upload endpoint's request delegate so that a handler's refusal raised inside tusdotnet's
    /// completion event is answered the way tusdotnet answers every other refusal: the status, the protocol
    /// version, and the reason as plain text.
    /// </summary>
    public static RequestDelegate AnswerRejection(RequestDelegate next)
    {
        return async httpContext =>
        {
            try
            {
                await next(httpContext).ConfigureAwait(false);
            }
            catch (TusUploadRejectedException rejection) when (!httpContext.Response.HasStarted)
            {
                var response = httpContext.Response;
                response.StatusCode = StatusCodes.Status400BadRequest;
                response.Headers[TusProtocol.ResumableHeader] = TusProtocol.Version;
                response.ContentType = "text/plain";

                // The refusal is decided, so it is written whether or not the client is still listening.
                await response.WriteAsync(rejection.Message, CancellationToken.None).ConfigureAwait(false);
            }
        };
    }

    /// <summary>Runs the handler for a complete upload.</summary>
    /// <returns>What the handler decided.</returns>
    private async Task<TusUploadHandoff> _DecideAsync(
        HttpContext httpContext,
        string fileId,
        Type handlerType,
        TusUploadStamp caller,
        CancellationToken cancellationToken
    )
    {
        var upload = await _DescribeAsync(fileId, caller, cancellationToken).ConfigureAwait(false);
        var handler = (ITusUploadCompletedHandler)httpContext.RequestServices.GetRequiredService(handlerType);

        return await handler.HandleAsync(upload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies what the handler decided: a refused upload is removed at once, and any other upload is recorded as
    /// handed off, then removed after the response when the handler asked for that.
    /// </summary>
    private async Task _ApplyAsync(
        HttpContext httpContext,
        string fileId,
        TusUploadStamp caller,
        TusUploadHandoff handoff
    )
    {
        if (handoff.IsRejected)
        {
            await remover.RemoveAsync(fileId).ConfigureAwait(false);

            return;
        }

        if (store is ITusHandoffStore handoffStore)
        {
            // Not tied to the request: the handler has returned, and a client that disconnects now must not make
            // it run a second time.
            await handoffStore.MarkHandedOffAsync(fileId, CancellationToken.None).ConfigureAwait(false);
        }

        if (ReferenceEquals(handoff, TusUploadHandoff.Delete))
        {
            // The request is still to be answered from the upload: a status request reports its offset, and a
            // creating request reads it for its 201.
            remover.RemoveAfterResponse(httpContext, fileId, caller.TenantId);
        }
    }

    private async Task<TusCompletedUpload> _DescribeAsync(
        string fileId,
        TusUploadStamp caller,
        CancellationToken cancellationToken
    )
    {
        var length =
            await store.GetUploadLengthAsync(fileId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The complete upload {fileId} has no length.");

        var rawMetadata = store is ITusCreationStore creationStore
            ? await creationStore.GetUploadMetadataAsync(fileId, cancellationToken).ConfigureAwait(false)
            : null;

        var metadata = MetadataParser
            .ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, TusUploadStamp.Strip(rawMetadata))
            .Metadata;

        return new TusCompletedUpload(contentToken => _OpenContentAsync(fileId, contentToken))
        {
            FileId = fileId,
            Length = length,
            Metadata = metadata.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.GetString(Encoding.UTF8),
                StringComparer.Ordinal
            ),
            TenantId = caller.TenantId,
            OwnerKey = caller.OwnerKey,
        };
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

    /// <summary>Whether the upload holds every byte of a known length, as tusdotnet decides it is complete.</summary>
    private async Task<bool> _HasAllBytesAsync(string fileId, CancellationToken cancellationToken)
    {
        var length = await store.GetUploadLengthAsync(fileId, cancellationToken).ConfigureAwait(false);

        return length is not null
            && length == await store.GetUploadOffsetAsync(fileId, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 3306,
        Level = LogLevel.Warning,
        Message = "{Endpoint} has a completion handler, and the upload store {Store} cannot record that an upload was handed off: the handler runs once per upload, and an upload whose handler fails is not handed to it again"
    )]
    private static partial void LogHandlerRunsOnce(ILogger logger, string endpoint, string store);

    [LoggerMessage(
        EventId = 3307,
        Level = LogLevel.Error,
        Message = "Could not complete upload {FileId} on a status request; answered 423 so the client asks again"
    )]
    private static partial void LogOwedHandlerFailed(ILogger logger, Exception exception, string fileId);
}
