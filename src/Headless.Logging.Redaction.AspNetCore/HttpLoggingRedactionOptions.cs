// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Net.Http.Headers;

namespace Headless.Logging;

/// <summary>
/// What ASP.NET Core's HTTP logging may write, for <c>AddHeadlessHttpLoggingRedaction</c>: which headers and route
/// parameters are logged, and how each value is redacted.
/// </summary>
/// <remarks>
/// HTTP logging writes no header and no route parameter value that is not listed here. A listed entry is logged by
/// name with its value passed through the redactor its classification maps to, so map a header you want to read in
/// full to a classification whose redactor keeps the value.
/// </remarks>
[PublicAPI]
public sealed class HttpLoggingRedactionOptions
{
    /// <summary>
    /// Request headers to log, by name, with their classification. Defaults to <c>Authorization</c>,
    /// <c>Proxy-Authorization</c>, and <c>Cookie</c> as <see cref="HeadlessDataClassifications.Secret"/>.
    /// </summary>
    public IDictionary<string, DataClassification> RequestHeaders { get; } =
        new Dictionary<string, DataClassification>(StringComparer.OrdinalIgnoreCase)
        {
            [HeaderNames.Authorization] = HeadlessDataClassifications.Secret,
            [HeaderNames.ProxyAuthorization] = HeadlessDataClassifications.Secret,
            [HeaderNames.Cookie] = HeadlessDataClassifications.Secret,
        };

    /// <summary>
    /// Response headers to log, by name, with their classification. Defaults to <c>Set-Cookie</c> as
    /// <see cref="HeadlessDataClassifications.Secret"/>.
    /// </summary>
    public IDictionary<string, DataClassification> ResponseHeaders { get; } =
        new Dictionary<string, DataClassification>(StringComparer.OrdinalIgnoreCase)
        {
            [HeaderNames.SetCookie] = HeadlessDataClassifications.Secret,
        };

    /// <summary>
    /// Route parameters to log, by name, with their classification. A parameter not listed is redacted in the logged
    /// path.
    /// </summary>
    public IDictionary<string, DataClassification> RouteParameters { get; } =
        new Dictionary<string, DataClassification>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Path prefixes HTTP logging skips entirely, such as <c>/health</c>.</summary>
    public ISet<string> ExcludePathStartsWith { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a request that matches no route is logged with its path. Defaults to <see langword="false"/>.</summary>
    public bool IncludeUnmatchedRoutes { get; set; }
}
