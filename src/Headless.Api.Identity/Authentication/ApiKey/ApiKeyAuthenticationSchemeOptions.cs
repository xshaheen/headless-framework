// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Http;
using Headless.Security;
using Microsoft.AspNetCore.Authentication;

namespace Headless.Api.Identity;

/// <summary>Options for the API key authentication scheme.</summary>
[PublicAPI]
public sealed class ApiKeyAuthenticationSchemeOptions
    : AuthenticationSchemeOptions,
        IHttpCredentialSchemeOptions,
        ICredentialCacheOptions
{
    /// <summary>Default scheme name used when none is specified during registration.</summary>
    public const string DefaultScheme = AuthenticationConstants.Schemas.ApiKey;

    /// <summary>Default query-string parameter name (<c>"api_key"</c>).</summary>
    public const string ParamName = "api_key";

    /// <summary>Default HTTP header name for the API key.</summary>
    public const string HeaderName = HttpHeaderNames.ApiKey;

    /// <summary>HTTP header name from which the API key is read. Defaults to <see cref="HeaderName"/>.</summary>
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

    /// <summary>
    /// How long a successful authentication is reused for the same API key without asking the
    /// <see cref="IApiKeyStore{TUser,TUserId}"/> and the Identity store again. <see langword="null"/> (the default) or
    /// <see cref="TimeSpan.Zero"/> turns the cache off, so every request runs the full check. At most one hour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// While an entry lives, a request with the same key skips the key-store lookup, the sign-in and lockout checks,
    /// and the claims and role queries, and gets its own copy of the principal the first request built. The entry
    /// expires a fixed time after the request that created it; hits do not extend it.
    /// </para>
    /// <para>
    /// <strong>Trade-off:</strong> a revoked or deactivated key keeps authenticating until its entry expires, and so
    /// does a user who has since been locked out or disabled, or whose roles or claims changed. Pick a lifetime the
    /// application can accept for that delay.
    /// </para>
    /// <para>
    /// Only successful authentications are cached: an unknown or rejected key always runs the full check. The cache is
    /// in-process and keyed by an HMAC of the scheme and the key under a per-process random key, so it never holds the
    /// key and does not survive a restart.
    /// </para>
    /// </remarks>
    public TimeSpan? CredentialCacheDuration { get; set; }

    /// <summary>
    /// Maximum number of cached authentications this scheme holds while <see cref="CredentialCacheDuration"/> is on.
    /// Default <c>1000</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A success that arrives while the cache is full is not cached; the cache then evicts expired entries, then the
    /// least recently used ones, in the background to make room.
    /// </para>
    /// </remarks>
    public int CredentialCacheMaxEntries { get; set; } = 1000;

    /// <inheritdoc/>
    /// <remarks>The <see cref="ApiKeyHeaderName"/> header, then the <see cref="ApiKeyParamName"/> query parameter when <see cref="AllowApiKeyInQueryString"/> is set.</remarks>
    public IReadOnlyList<HttpCredentialDescription> Credentials =>
        AllowApiKeyInQueryString
            ?
            [
                _HeaderCredential(),
                HttpCredentialDescription.Query(
                    ApiKeyParamName,
                    $"The API key in the {ApiKeyParamName} query parameter, read only when the {ApiKeyHeaderName} header is absent. Servers and proxies log query strings, so prefer the header."
                ),
            ]
            : [_HeaderCredential()];

    private HttpCredentialDescription _HeaderCredential()
    {
        return HttpCredentialDescription.Header(ApiKeyHeaderName, $"The API key in the {ApiKeyHeaderName} header.");
    }
}

internal sealed class ApiKeyAuthenticationSchemeOptionsValidator : AbstractValidator<ApiKeyAuthenticationSchemeOptions>
{
    public ApiKeyAuthenticationSchemeOptionsValidator()
    {
        Include(new CredentialCacheOptionsValidator());
    }
}
