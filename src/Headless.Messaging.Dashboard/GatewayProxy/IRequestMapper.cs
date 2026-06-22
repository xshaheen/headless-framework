// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Messaging.Dashboard.GatewayProxy;

/// <summary>
/// Maps an incoming ASP.NET Core <see cref="HttpRequest"/> to an outbound
/// <see cref="HttpRequestMessage"/> for forwarding to a peer messaging node.
/// </summary>
public interface IRequestMapper
{
    /// <summary>
    /// Converts the incoming request — including method, headers, and body — into an
    /// <see cref="HttpRequestMessage"/> ready to be sent via <see cref="System.Net.Http.HttpClient"/>.
    /// </summary>
    /// <param name="request">The original inbound HTTP request to map.</param>
    /// <returns>
    /// A new <see cref="HttpRequestMessage"/> populated from <paramref name="request"/>.
    /// The caller is responsible for setting the destination <see cref="HttpRequestMessage.RequestUri"/>
    /// before sending.
    /// </returns>
    Task<HttpRequestMessage> Map(HttpRequest request);
}
