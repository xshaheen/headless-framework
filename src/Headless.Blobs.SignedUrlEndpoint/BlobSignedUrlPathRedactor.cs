// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

/// <summary>
/// Replaces the token in a signed blob URL's path with <c>***</c>. The token is the only credential the endpoint asks
/// for, so a path that reaches a log or a trace would let anyone who reads it download or overwrite the blob until the
/// URL expires.
/// </summary>
internal sealed class BlobSignedUrlPathRedactor
{
    public const string RedactedSegment = "***";

    // Every place the request path can reach a trace. url.path is what ASP.NET Core and the OpenTelemetry
    // instrumentation set today; http.target (older semantic conventions) and url.full come from other instrumentation.
    private static readonly string[] _PathTags = ["url.path", "http.target", "url.full"];

    private readonly PathString[] _prefixes;

    public BlobSignedUrlPathRedactor(IOptions<BlobSignedUrlOptions> options)
    {
        var settings = options.Value;
        var routePrefix = new PathString(settings.RoutePrefix.TrimEnd('/'));
        var publicPathBase = settings.BaseUrl?.AbsolutePath.TrimEnd('/');

        // A URL minted under a public path base (https://example.com/app) reaches the app as /app/blobs/{token} until
        // UsePathBase moves /app into PathBase, which may happen after the point that logs or tags the path.
        _prefixes = string.IsNullOrEmpty(publicPathBase)
            ? [routePrefix]
            : [routePrefix, new PathString(publicPathBase).Add(routePrefix)];
    }

    /// <summary>
    /// Returns <paramref name="path"/> with everything below the route prefix replaced by <c>***</c>, when the path
    /// is under the signed-URL route prefix and carries a token.
    /// </summary>
    public bool TryRedact(PathString path, out PathString redacted)
    {
        // Routing matches paths case-insensitively, so /BLOBS/{token} reaches the endpoint too. The whole remainder is
        // replaced, not only its first segment: the route has one segment, and anything longer is no valid URL that
        // is worth logging in full.
        foreach (var prefix in _prefixes)
        {
            if (
                path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase, out var matched, out var remaining)
                && remaining.Value is { Length: > 1 }
            )
            {
                redacted = matched.Add(new PathString("/" + RedactedSegment));

                return true;
            }
        }

        redacted = default;

        return false;
    }

    /// <summary>
    /// Rewrites the path tags of the request's server activity, and of the current activity when instrumentation
    /// started a separate one, when the request targets a signed URL.
    /// </summary>
    public void RedactActivities(HttpContext context)
    {
        var request = context.Request;

        if (!TryRedact(request.Path, out var redactedPath))
        {
            return;
        }

        // Matches how ASP.NET Core and the OpenTelemetry instrumentation build url.path.
        var raw = request.PathBase.Add(request.Path).ToString();
        var redacted = request.PathBase.Add(redactedPath).ToString();

        var serverActivity = context.Features.Get<IHttpActivityFeature>()?.Activity;
        _RedactTags(serverActivity, raw, redacted);

        // The OpenTelemetry instrumentation exports a sibling of the server activity, and sets the tags on it, when
        // the incoming trace context does not match the one ASP.NET Core extracted.
        if (Activity.Current is { } current && !ReferenceEquals(current, serverActivity))
        {
            _RedactTags(current, raw, redacted);
        }
    }

    private static void _RedactTags(Activity? activity, string raw, string redacted)
    {
        if (activity is null)
        {
            return;
        }

        foreach (var tag in _PathTags)
        {
            if (activity.GetTagItem(tag) is string value && value.Contains(raw, StringComparison.Ordinal))
            {
                activity.SetTag(tag, value.Replace(raw, redacted, StringComparison.Ordinal));
            }
        }
    }
}
