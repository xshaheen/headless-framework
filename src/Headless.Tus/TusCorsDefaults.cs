// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Cors.Infrastructure;
using tusdotnet.Constants;

namespace Headless.Tus;

/// <summary>
/// The CORS values a browser-based tus client needs. Browsers hide response headers from
/// cross-origin JavaScript unless they are explicitly exposed, so a tus endpoint consumed from
/// another origin (an SPA dev server, a CDN-hosted frontend) must expose the tus response
/// headers — otherwise clients like <c>tus-js-client</c> and Uppy cannot read
/// <c>Location</c>/<c>Upload-Offset</c> and every upload fails on the first request.
/// </summary>
[PublicAPI]
public static class TusCorsDefaults
{
    /// <summary>
    /// Response headers a cross-origin tus client must be able to read
    /// (<c>Access-Control-Expose-Headers</c>): the creation <c>Location</c>, the protocol/version
    /// negotiation headers, and every <c>Upload-*</c> state header.
    /// </summary>
    public static IReadOnlyList<string> ExposedHeaders { get; } =
    [
        "Location",
        HeaderConstants.TusResumable,
        HeaderConstants.TusVersion,
        HeaderConstants.TusExtension,
        HeaderConstants.TusMaxSize,
        HeaderConstants.TusChecksumAlgorithm,
        HeaderConstants.UploadOffset,
        HeaderConstants.UploadLength,
        HeaderConstants.UploadDeferLength,
        HeaderConstants.UploadMetadata,
        HeaderConstants.UploadExpires,
        HeaderConstants.UploadConcat,
    ];

    /// <summary>
    /// Request headers a tus client sends (<c>Access-Control-Allow-Headers</c>): the protocol
    /// header, the <c>Upload-*</c> request headers, the PATCH content type, and
    /// <c>X-HTTP-Method-Override</c> for clients behind proxies that block PATCH/DELETE.
    /// </summary>
    public static IReadOnlyList<string> AllowedHeaders { get; } =
    [
        HeaderConstants.TusResumable,
        HeaderConstants.UploadLength,
        HeaderConstants.UploadDeferLength,
        HeaderConstants.UploadOffset,
        HeaderConstants.UploadMetadata,
        HeaderConstants.UploadChecksum,
        HeaderConstants.UploadConcat,
        HeaderConstants.XHttpMethodOveride,
        HeaderConstants.ContentType,
    ];

    /// <summary>HTTP methods the tus 1.0.0 protocol uses (<c>Access-Control-Allow-Methods</c>).</summary>
    public static IReadOnlyList<string> AllowedMethods { get; } = ["GET", "POST", "HEAD", "PATCH", "DELETE", "OPTIONS"];
}

/// <summary>CORS policy helpers for tus endpoints.</summary>
[PublicAPI]
public static class TusCorsPolicyBuilderExtensions
{
    extension(CorsPolicyBuilder policy)
    {
        /// <summary>
        /// Applies the tus protocol's CORS surface to the policy: allowed request headers,
        /// exposed response headers, and allowed methods (see <see cref="TusCorsDefaults"/>).
        /// Origins and credentials remain the caller's decision.
        /// </summary>
        /// <returns>the same builder for chaining</returns>
        public CorsPolicyBuilder WithTusHeaders()
        {
            return policy
                .WithHeaders([.. TusCorsDefaults.AllowedHeaders])
                .WithExposedHeaders([.. TusCorsDefaults.ExposedHeaders])
                .WithMethods([.. TusCorsDefaults.AllowedMethods]);
        }
    }
}
