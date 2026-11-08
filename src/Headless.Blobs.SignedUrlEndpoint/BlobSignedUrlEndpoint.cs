// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internal;
using Headless.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Headless.Blobs;

/// <summary>Request handlers behind <c>MapBlobSignedUrlEndpoint</c>.</summary>
internal static class BlobSignedUrlEndpoint
{
    public static async Task<IResult> DownloadAsync(
        string token,
        HttpContext context,
        BlobSignedUrlSigner signer,
        IMimeTypeProvider mimeTypeProvider
    )
    {
        if (_Authorize(token, BlobSignedUrlAccess.Download, context, signer) is not ({ } storage, { } grant))
        {
            return Results.NotFound();
        }

        var download = await storage.OpenReadStreamAsync(grant.Location, context.RequestAborted);

        if (download is null)
        {
            return Results.NotFound();
        }

        // The bytes are served from the application's own origin, unlike a cloud presigned URL. Without these headers
        // an uploaded HTML or SVG file would run script with the application's cookies and origin.
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        context.Response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox";

        // FileSystem, Redis, and SFTP store no content type, so derive it from the key the same way UploadAsync does
        // for a null content type. An attachment disposition names the saved file after the blob rather than the
        // opaque token, and keeps a top-level navigation from rendering the bytes in the application's origin at all;
        // browsers ignore it for subresources, so <img> and similar embeds still work. A file name signed into the
        // grant replaces the blob's name, written with the same header builder the cloud providers sign.
        string? fileDownloadName = null;

        if (grant.FileName is { } fileName)
        {
            context.Response.Headers[HeaderNames.ContentDisposition] =
                BlobStorageHelpers.BuildAttachmentContentDisposition(fileName);
        }
        else
        {
            fileDownloadName = _FileName(grant.Location.Path);
        }

        return Results.Stream(
            download.Stream,
            grant.ContentType ?? mimeTypeProvider.GetMimeType(grant.Location.Path),
            fileDownloadName,
            enableRangeProcessing: download.Stream.CanSeek
        );
    }

    public static async Task<IResult> UploadAsync(string token, HttpContext context, BlobSignedUrlSigner signer)
    {
        if (_Authorize(token, BlobSignedUrlAccess.Upload, context, signer) is not ({ } storage, { } grant))
        {
            return Results.NotFound();
        }

        var request = context.Request;

        if (grant.ContentType is not null && !_IsSameMediaType(request.ContentType, grant.ContentType))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (grant.MaxLength is { } maxLength)
        {
            // A declared length lets an oversized upload be refused before any byte reaches the store; a chunked body
            // could only be cut off midway, after the provider has started writing. A request that declares both is
            // framed by its chunks, so its Content-Length proves nothing and it is refused the same way.
            if (request.ContentLength is not { } contentLength || request.Headers.TransferEncoding.Count > 0)
            {
                return Results.StatusCode(StatusCodes.Status411LengthRequired);
            }

            if (contentLength > maxLength)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            // Raise or lower the server's body limit to the token's, so the server default neither rejects an allowed
            // upload nor admits a larger one.
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            {
                bodySize.MaxRequestBodySize = maxLength;
            }
        }

        // Every provider replaces a blob only once its whole body has arrived, so an aborted or over-limit upload leaves
        // the earlier version in place and there is nothing to clean up. The exception propagates so Kestrel answers
        // an over-limit body with its own 413.
        await storage.UploadAsync(
            grant.Location,
            request.Body,
            contentType: grant.ContentType ?? request.ContentType,
            metadata: null,
            context.RequestAborted
        );

        return Results.Ok();
    }

    // Returns the grant and the store to serve it from, or null for any failure, after logging the outcome.
    private static (IBlobStorage Storage, BlobSignedUrlGrant Grant)? _Authorize(
        string token,
        BlobSignedUrlAccess access,
        HttpContext context,
        BlobSignedUrlSigner signer
    )
    {
        var status = signer.Read(token, access, out var grant);
        var storage =
            status == BlobSignedUrlTokenStatus.Valid ? _ResolveStorage(context.RequestServices, grant!.Store) : null;

        var outcome = status switch
        {
            BlobSignedUrlTokenStatus.Valid when storage is null => "store-not-found",
            BlobSignedUrlTokenStatus.Valid => "accepted",
            BlobSignedUrlTokenStatus.Unreadable => "unreadable",
            BlobSignedUrlTokenStatus.WrongOperation => "wrong-operation",
            BlobSignedUrlTokenStatus.Expired => "expired",
            _ => "rejected",
        };

        _Log(context, token, access, outcome, grant);

        return storage is null ? null : (storage, grant!);
    }

    private static void _Log(
        HttpContext context,
        string token,
        BlobSignedUrlAccess access,
        string outcome,
        BlobSignedUrlGrant? grant
    )
    {
        var services = context.RequestServices;

        if (
            services.GetService<ILoggerFactory>()?.CreateLogger(BlobSignedUrlAccessLog.Category) is not { } logger
            || !logger.IsEnabled(LogLevel.Information)
        )
        {
            return;
        }

        var fingerprint = BlobSignedUrlAccessLog.Fingerprint(token);
        var logLocation = services.GetService<IOptions<BlobSignedUrlOptions>>()?.Value.LogBlobLocation == true;

        if (logLocation && grant is not null)
        {
            logger.LogSignedUrlAccess(
                access,
                outcome,
                fingerprint,
                grant.ExpiresAt,
                grant.Location.Container,
                grant.Location.Path
            );
        }
        else
        {
            logger.LogSignedUrlAccess(access, outcome, fingerprint, grant?.ExpiresAt);
        }
    }

    private static string _FileName(string path)
    {
        return path[(path.LastIndexOf('/') + 1)..];
    }

    private static IBlobStorage? _ResolveStorage(IServiceProvider services, string? store)
    {
        var storage = store is null
            ? services.GetService<IBlobStorage>()
            : services.GetService<IBlobStorageProvider>()?.GetStorageOrNull(store);

        // A grant carries the physical location the store resolved when the URL was minted, already rewritten by a
        // scoped store such as tenant scoping, and its signature is the authorization. Scoping it again under whatever
        // tenant the request carries would prefix the tenant twice, or refuse an anonymous request outright.
        return storage is IScopedBlobStorage scoped ? scoped.Unscoped : storage;
    }

    private static bool _IsSameMediaType(string? actual, string expected)
    {
        return MediaTypeHeaderValue.TryParse(actual, out var actualType)
            && MediaTypeHeaderValue.TryParse(expected, out var expectedType)
            && actualType.MediaType.Equals(expectedType.MediaType, StringComparison.OrdinalIgnoreCase);
    }
}
