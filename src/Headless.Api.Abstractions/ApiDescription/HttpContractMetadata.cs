// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Endpoint metadata describing HTTP behavior that the endpoint's own signature does not show: request headers it
/// accepts, responses it may return, and response headers those responses may carry. A feature that runs around the
/// handler, such as a middleware or a filter, attaches it so API documentation can describe the feature without
/// depending on it.
/// </summary>
/// <remarks>
/// <para>
/// It describes only what framework features write or read on the wire; it is not a general OpenAPI model.
/// </para>
/// <para>
/// An endpoint may carry several instances; consumers apply each of them. It describes the contract only, so nothing
/// enforces it at run time. Headless.OpenApi renders it as header parameters, problem-details responses, and response
/// headers. An entry an endpoint already declares by itself (a bound header parameter, a declared response, or a
/// response header) keeps its own description.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HttpContractMetadata
{
    /// <summary>The request headers the endpoint reads. Default is empty.</summary>
    public IReadOnlyList<HttpRequestHeaderDescription> RequestHeaders { get; init; } = [];

    /// <summary>The responses the endpoint may return besides the handler's own. Default is empty.</summary>
    public IReadOnlyList<HttpResponseDescription> Responses { get; init; } = [];

    /// <summary>The headers the endpoint's responses may carry. Default is empty.</summary>
    public IReadOnlyList<HttpResponseHeaderDescription> ResponseHeaders { get; init; } = [];
}
