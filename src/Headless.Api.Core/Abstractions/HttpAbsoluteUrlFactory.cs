// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api.Abstractions;

/// <summary>
/// Builds absolute URLs from relative paths using the current HTTP request's scheme, host, and path base.
/// </summary>
/// <remarks>
/// Register the HTTP implementation via the framework's DI setup. For contexts outside an active
/// HTTP request (background workers, event handlers), inject <see cref="IAbsoluteUrlFactory"/> and
/// guard on <see cref="Origin"/> availability, or construct URLs via an ambient base URL instead.
/// </remarks>
public interface IAbsoluteUrlFactory
{
    /// <summary>
    /// Gets the scheme and host of the current request (e.g., <c>https://api.example.com:5001</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">No active HTTP request is available.</exception>
    string Origin { get; }

    /// <summary>
    /// Builds an absolute URL from a relative <paramref name="path"/> using the current HTTP context.
    /// Returns <see langword="null"/> if <paramref name="path"/> is <see langword="null"/> or not a
    /// well-formed relative URI. Returns <paramref name="path"/> unchanged if it is already absolute.
    /// </summary>
    /// <param name="path">A relative path (e.g., <c>/api/items/42</c>) or an already-absolute URL.</param>
    /// <returns>
    /// An absolute URL, the original <paramref name="path"/> if already absolute, or
    /// <see langword="null"/> if <paramref name="path"/> is malformed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No active HTTP request is available and <paramref name="path"/> requires one to resolve.
    /// </exception>
    string? GetAbsoluteUrl(string path);

    /// <summary>
    /// Builds an absolute URL from a relative <paramref name="path"/> using an explicit
    /// <paramref name="context"/>. Prefer this overload when an <see cref="HttpContext"/> is
    /// already in scope to avoid an <see cref="IHttpContextAccessor"/> lookup.
    /// Returns <see langword="null"/> if <paramref name="path"/> is <see langword="null"/> or not a
    /// well-formed relative URI. Returns <paramref name="path"/> unchanged if it is already absolute.
    /// </summary>
    /// <param name="context">The current <see cref="HttpContext"/>.</param>
    /// <param name="path">A relative path (e.g., <c>/api/items/42</c>) or an already-absolute URL.</param>
    /// <returns>
    /// An absolute URL, the original <paramref name="path"/> if already absolute, or
    /// <see langword="null"/> if <paramref name="path"/> is malformed.
    /// </returns>
    string? GetAbsoluteUrl(HttpContext context, string path);
}

internal sealed class HttpAbsoluteUrlFactory(IHttpContextAccessor accessor) : IAbsoluteUrlFactory
{
    private static readonly string[] _Separator = ["://"];

    public string Origin
    {
        get
        {
            if (accessor.HttpContext?.Request is null)
            {
                throw new InvalidOperationException(
                    "The request is not currently available. This service can only be used within the context of an existing HTTP request."
                );
            }

            var request = accessor.HttpContext.Request;

            return request.Scheme + "://" + request.Host.ToUriComponent();
        }
        set
        {
            if (accessor.HttpContext?.Request is null)
            {
                throw new InvalidOperationException(
                    "The request is not currently available. This service can only be used within the context of an existing HTTP request."
                );
            }

            var split = value.Split(_Separator, StringSplitOptions.RemoveEmptyEntries);

            if (split.Length < 2)
            {
                throw new ArgumentException(
                    "Origin must contain a scheme and host separated by '://' (e.g., 'https://example.com').",
                    nameof(value)
                );
            }

            var request = accessor.HttpContext.Request;
            request.Scheme = split[0];
            request.Host = new HostString(split[^1]);
        }
    }

    public string? GetAbsoluteUrl(string path)
    {
        var (process, result) = _ShouldProcessPath(path);

        if (!process)
        {
            return result;
        }

        if (accessor.HttpContext?.Request is null)
        {
            throw new InvalidOperationException(
                "The request is not currently available. This service can only be used within the context of an existing HTTP request."
            );
        }

        return GetAbsoluteUrl(accessor.HttpContext, path);
    }

    public string? GetAbsoluteUrl(HttpContext context, string path)
    {
        var (process, result) = _ShouldProcessPath(path);

        if (!process)
        {
            return result;
        }

        var request = context.Request;

        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}{path}";
    }

    private static (bool process, string? result) _ShouldProcessPath(string? path)
    {
        if (path is null || !Uri.IsWellFormedUriString(path, UriKind.RelativeOrAbsolute))
        {
            return (process: false, result: null);
        }

        return (process: !Uri.IsWellFormedUriString(path, UriKind.Absolute), result: path);
    }
}
