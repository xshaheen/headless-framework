// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Framework.Api.Abstractions;

public interface IAbsoluteUrlFactory
{
    /// <summary>Gets or sets the origin for the server. For example, "https://server.acme.com:5001".</summary>
    /// <exception cref="InvalidOperationException">The HttpContext is not available.</exception>
    string Origin { get; set; }

    /// <summary>Call this method when you're overriding a service that doesn't have an HttpContext instance available.</summary>
    /// <param name="path">Relative path.</param>
    /// <returns>Returns an absolute path or null if the <paramref name="path"/> is not well-formed relative path.</returns>
    /// <exception cref="InvalidOperationException">The HttpContext is not available.</exception>
    string? GetAbsoluteUrl(string path);

    /// <summary>Call this method when you are implementing a service that has an HttpContext instance available.</summary>
    /// <param name="context">HttpContext</param>
    /// <param name="path">Relative path.</param>
    /// <returns>Returns an absolute path or null if the <paramref name="path"/> is not well-formed relative path.</returns>
    string? GetAbsoluteUrl(HttpContext context, string path);
}

public sealed class HttpAbsoluteUrlFactory(IHttpContextAccessor accessor) : IAbsoluteUrlFactory
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
