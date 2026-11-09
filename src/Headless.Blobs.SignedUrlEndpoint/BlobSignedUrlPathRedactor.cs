// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

/// <summary>
/// Replaces the token in a signed blob URL's path with <c>***</c>. The token is the only credential the endpoint asks
/// for, so a path that reaches a log or a trace would let anyone who reads it download or overwrite the blob until the
/// URL expires.
/// </summary>
/// <remarks>
/// <c>UseSignedUrlEndpoint</c> registers it as a singleton and applies it to HTTP logging, request activities, and
/// ASP.NET Core's hosting request logs. Resolve it to redact a path the framework does not log itself, such as the
/// <c>RequestPath</c> property of Serilog request logging (<c>UseSerilogRequestLogging</c>).
/// </remarks>
[PublicAPI]
public sealed class BlobSignedUrlPathRedactor : ILogPathRedactor
{
    /// <summary>The text that replaces everything below the route prefix.</summary>
    public const string RedactedSegment = "***";

    private readonly PathString[] _prefixes;

    /// <summary>Creates a redactor for the configured route prefix and public base URL.</summary>
    /// <param name="options">The endpoint options.</param>
    public BlobSignedUrlPathRedactor(IOptions<BlobSignedUrlOptions> options)
    {
        Argument.IsNotNull(options);

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
    /// is under the signed-URL route prefix, or under the public base URL's path followed by it, and carries a token.
    /// The prefix matches case-insensitively, as routing does.
    /// </summary>
    /// <param name="path">The request path, with or without the public path base.</param>
    /// <param name="redacted">The redacted path, or the default value when nothing was redacted.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> carried a token.</returns>
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

    /// <inheritdoc />
    bool ILogPathRedactor.TryRedact(string path, [NotNullWhen(true)] out string? redacted)
    {
        if (TryRedact(new PathString(path), out var redactedPath))
        {
            redacted = redactedPath.ToString();

            return true;
        }

        redacted = null;

        return false;
    }
}
