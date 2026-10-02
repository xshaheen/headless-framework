// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Cors.Infrastructure;
using tusdotnet.Helpers;

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
    /// <remarks>Same list as tusdotnet's <c>CorsHelper.GetExposedHeaders()</c>.</remarks>
    public static IReadOnlyList<string> ExposedHeaders { get; } = [.. CorsHelper.GetExposedHeaders()];

    /// <summary>
    /// Request headers a tus client sends (<c>Access-Control-Allow-Headers</c>): the protocol
    /// header, the <c>Upload-*</c> request headers, the PATCH content type, and
    /// <c>X-HTTP-Method-Override</c> for clients behind proxies that block PATCH/DELETE.
    /// </summary>
    /// <remarks>Same list as tusdotnet's <c>CorsHelper.GetAllowedHeaders()</c>.</remarks>
    public static IReadOnlyList<string> AllowedHeaders { get; } = [.. CorsHelper.GetAllowedHeaders()];

    /// <summary>HTTP methods the tus 1.0.0 protocol uses (<c>Access-Control-Allow-Methods</c>), plus <c>GET</c>.</summary>
    /// <remarks>
    /// tusdotnet's <c>CorsHelper.GetAllowedMethods()</c> plus <c>GET</c>: a readable store
    /// (<c>ITusReadableStore</c>) is usually served from a download endpoint under the same CORS policy.
    /// </remarks>
    public static IReadOnlyList<string> AllowedMethods { get; } = [.. CorsHelper.GetAllowedMethods(), "GET"];
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
