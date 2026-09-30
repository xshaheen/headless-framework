// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api.Cors;

/// <summary>
/// Decides at request time whether a browser origin missing from a policy's static lists may call the API, such as a
/// tenant's custom domain loaded from a catalog.
/// </summary>
/// <remarks>
/// Register one per policy with <c>AddHeadlessCorsOriginSource&lt;TSource&gt;(policyName)</c>. It is resolved from
/// the request's services, so a scoped implementation may use a <c>DbContext</c>. It runs only for a request that
/// carries exactly one <c>Origin</c> header that the policy's <see cref="HeadlessCorsOptions.AllowedOrigins"/> and
/// <see cref="HeadlessCorsOptions.AllowedOriginTemplates"/> do not already admit, and never for the opaque origin
/// <c>Origin: null</c>. Both the preflight and the actual request consult it, so cache lookups instead of querying a
/// store on every call. An approved origin receives the policy's headers, methods, and credentials setting unchanged.
/// </remarks>
[PublicAPI]
public interface ICorsOriginSource
{
    /// <summary>Returns whether <paramref name="origin"/> may call the API under the policy this source serves.</summary>
    /// <param name="origin">The raw <c>Origin</c> request header, such as <c>https://shop.acme.com</c>.</param>
    /// <param name="context">The current request.</param>
    /// <param name="cancellationToken">Cancelled when the request is aborted.</param>
    /// <returns><see langword="true"/> to allow the origin; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> IsOriginAllowedAsync(string origin, HttpContext context, CancellationToken cancellationToken);
}
