// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Microsoft.AspNetCore.Authentication;

namespace Headless.Api.Identity.Authentication.ApiKey;

[PublicAPI]
public sealed class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "API Key Authentication";
    public const string ParamName = "api_key";
    public const string HeaderName = HttpHeaderNames.ApiKey;

    public string ApiKeyHeaderName { get; set; } = HeaderName;

    /// <summary>
    /// When <see langword="true"/>, the handler also accepts the API key from the URL query string
    /// (parameter name controlled by <see cref="ApiKeyParamName"/>).
    /// <para>
    /// <strong>Security note:</strong> query-string values are recorded in server access logs,
    /// CDN/proxy logs, and <c>Referer</c> headers sent by browsers, which can unintentionally
    /// expose the key. Enable this only for clients that cannot set HTTP headers (e.g. browser
    /// <c>&lt;img&gt;</c> or webhook callers). Header-based auth is the secure default.
    /// </para>
    /// </summary>
    public bool AllowApiKeyInQueryString { get; set; }

    /// <summary>
    /// Name of the URL query-string parameter used to pass the API key when
    /// <see cref="AllowApiKeyInQueryString"/> is <see langword="true"/>.
    /// Defaults to <c>"api_key"</c>.
    /// </summary>
    public string ApiKeyParamName { get; set; } = ParamName;
}
