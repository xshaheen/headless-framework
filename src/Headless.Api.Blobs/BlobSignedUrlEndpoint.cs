// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
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
        if (
            !signer.TryRead(token, BlobSignedUrlAccess.Download, out var grant)
            || _ResolveStorage(context.RequestServices, grant.Store) is not { } storage
        )
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
        // for a null content type.
        return Results.Stream(
            download.Stream,
            mimeTypeProvider.GetMimeType(grant.Location.Path),
            enableRangeProcessing: download.Stream.CanSeek
        );
    }

    public static async Task<IResult> UploadAsync(string token, HttpContext context, BlobSignedUrlSigner signer)
    {
        if (
            !signer.TryRead(token, BlobSignedUrlAccess.Upload, out var grant)
            || _ResolveStorage(context.RequestServices, grant.Store) is not { } storage
        )
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
            // could only be cut off midway, after the provider has started writing.
            if (request.ContentLength is not { } contentLength)
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

        await storage.UploadAsync(
            grant.Location,
            request.Body,
            metadata: null,
            contentType: grant.ContentType ?? request.ContentType,
            context.RequestAborted
        );

        return Results.Ok();
    }

    private static IBlobStorage? _ResolveStorage(IServiceProvider services, string? store)
    {
        return store is null
            ? services.GetService<IBlobStorage>()
            : services.GetService<IBlobStorageProvider>()?.GetStorageOrNull(store);
    }

    private static bool _IsSameMediaType(string? actual, string expected)
    {
        return MediaTypeHeaderValue.TryParse(actual, out var actualType)
            && MediaTypeHeaderValue.TryParse(expected, out var expectedType)
            && actualType.MediaType.Equals(expectedType.MediaType, StringComparison.OrdinalIgnoreCase);
    }
}
