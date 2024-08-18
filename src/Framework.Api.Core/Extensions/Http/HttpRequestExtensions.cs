using System.Net;
using Framework.Arguments;
using Microsoft.Net.Http.Headers;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

/// <summary><see cref="HttpRequest"/> extension methods.</summary>
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

    public static bool CanAccept(this HttpRequest request, string contentType)
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(contentType);

        return request.Headers[HeaderNames.Accept].ToString().Contains(contentType, StringComparison.OrdinalIgnoreCase);
    }
}
