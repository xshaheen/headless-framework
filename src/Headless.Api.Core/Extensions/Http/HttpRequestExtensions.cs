// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Microsoft.Net.Http.Headers;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

/// <summary><see cref="HttpRequest"/> extension methods.</summary>
[PublicAPI]
public static class HttpRequestExtensions
{
    private const string _XmlHttpRequest = "XMLHttpRequest";

    /// <summary>Determines whether the specified HTTP request is an AJAX request.</summary>
    /// <param name="request">The HTTP request.</param>
    /// <returns><see langword="true"/> if the specified HTTP request is an AJAX request; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">The <paramref name="request"/> parameter is <see langword="null"/>.</exception>
    public static bool IsAjaxRequest(this HttpRequest request)
    {
        Argument.IsNotNull(request);

        return string.Equals(request.Query[HeaderNames.XRequestedWith], _XmlHttpRequest, StringComparison.Ordinal)
            || string.Equals(request.Headers.XRequestedWith, _XmlHttpRequest, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether the specified HTTP request is a local request where the IP address of the request
    /// originator was 127.0.0.1.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <returns><see langword="true"/> if the specified HTTP request is a local request; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">The <paramref name="request"/> parameter is <see langword="null"/>.</exception>
    public static bool IsLocalRequest(this HttpRequest request)
    {
        Argument.IsNotNull(request);

        var connection = request.HttpContext.Connection;

        if (connection.RemoteIpAddress is not null)
        {
            return connection.LocalIpAddress is not null
                ? connection.RemoteIpAddress.Equals(connection.LocalIpAddress)
                : IPAddress.IsLoopback(connection.RemoteIpAddress);
        }

        // for in memory TestServer or when dealing with default connection info
        return connection.RemoteIpAddress is null && connection.LocalIpAddress is null;
    }

    /// <summary>
    /// Determines whether the request's <c>Accept</c> header indicates acceptance of at least one of
    /// the supplied content types. A missing or empty <c>Accept</c> header is treated as accepting
    /// everything (per RFC 7231 §5.3.2).
    /// </summary>
    /// <param name="request">The HTTP request to inspect.</param>
    /// <param name="contentTypes">One or more MIME types to check acceptance of.</param>
    /// <returns>
    /// <see langword="true"/> if the <c>Accept</c> header is absent, or the best matching media range
    /// for at least one supplied content type has a positive quality value; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="contentTypes"/> is empty.</exception>
    public static bool CanAccept(this HttpRequest request, params ReadOnlySpan<string> contentTypes)
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(contentTypes);

        var acceptHeader = request.Headers[HeaderNames.Accept];

        if (acceptHeader.Count == 0)
        {
            return true;
        }

        // ParseList handles comma-separated values across all header entries.
        var parsed = MediaTypeHeaderValue.ParseList(acceptHeader);

        if (parsed is null or { Count: 0 })
        {
            return true;
        }

        foreach (var contentType in contentTypes)
        {
            if (MediaTypeHeaderValue.TryParse(contentType, out var candidate) && _CanAccept(parsed, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the request's <c>Accept</c> header indicates acceptance of the supplied
    /// content type. A missing or empty <c>Accept</c> header is treated as accepting everything
    /// (per RFC 7231 §5.3.2). Prefer the <c>ReadOnlySpan&lt;string&gt;</c> overload when checking
    /// multiple types at once.
    /// </summary>
    /// <param name="request">The HTTP request to inspect.</param>
    /// <param name="contentType">The MIME type to check acceptance of.</param>
    /// <returns>
    /// <see langword="true"/> if the <c>Accept</c> header is absent, or the best matching media range
    /// for <paramref name="contentType"/> has a positive quality value; otherwise <see langword="false"/>.
    /// Returns <see langword="false"/> when <paramref name="contentType"/> cannot be parsed as a
    /// valid media type.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or <paramref name="contentType"/> is <see langword="null"/>.</exception>
    public static bool CanAccept(this HttpRequest request, string contentType)
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(contentType);

        var acceptHeader = request.Headers[HeaderNames.Accept];

        if (acceptHeader.Count == 0)
        {
            return true;
        }

        if (!MediaTypeHeaderValue.TryParse(contentType, out var candidate))
        {
            return false;
        }

        var parsed = MediaTypeHeaderValue.ParseList(acceptHeader);

        if (parsed is null or { Count: 0 })
        {
            return true;
        }

        return _CanAccept(parsed, candidate);
    }

    internal static bool HasAcceptRejection(this HttpRequest request, string contentType)
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(contentType);

        var acceptHeader = request.Headers[HeaderNames.Accept];

        if (acceptHeader.Count == 0)
        {
            return false;
        }

        if (!MediaTypeHeaderValue.TryParse(contentType, out var candidate))
        {
            return false;
        }

        var parsed = MediaTypeHeaderValue.ParseList(acceptHeader);

        if (parsed is null or { Count: 0 })
        {
            return false;
        }

        var match = _GetBestAcceptMatch(parsed, candidate);

        return match.Found && match.Quality <= 0;
    }

    private static bool _CanAccept(IList<MediaTypeHeaderValue> acceptHeader, MediaTypeHeaderValue candidate)
    {
        var match = _GetBestAcceptMatch(acceptHeader, candidate);

        return match.Found && match.Quality > 0;
    }

    private static (bool Found, double Quality) _GetBestAcceptMatch(
        IList<MediaTypeHeaderValue> acceptHeader,
        MediaTypeHeaderValue candidate
    )
    {
        double bestQuality = 0;
        var bestSpecificity = -1;

        foreach (var mediaType in acceptHeader)
        {
            if (!candidate.IsSubsetOf(mediaType))
            {
                continue;
            }

            var specificity = _GetSpecificity(mediaType, candidate);
            var quality = mediaType.Quality.GetValueOrDefault(1);

            if (specificity > bestSpecificity || (specificity == bestSpecificity && quality > bestQuality))
            {
                bestSpecificity = specificity;
                bestQuality = quality;
            }
        }

        return (bestSpecificity >= 0, bestQuality);
    }

    private static int _GetSpecificity(MediaTypeHeaderValue mediaType, MediaTypeHeaderValue candidate)
    {
        if (mediaType.MatchesAllTypes)
        {
            return 0;
        }

        if (mediaType.MatchesAllSubTypes || mediaType.MatchesAllSubTypesWithoutSuffix)
        {
            return 1;
        }

        return string.Equals(mediaType.MediaType.Value, candidate.MediaType.Value, StringComparison.OrdinalIgnoreCase)
            ? 3
            : 2;
    }
}
