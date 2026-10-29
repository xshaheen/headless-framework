// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.Security;

/// <summary>
/// The key material and expected claims a JWT bearer scheme registered by <c>AddHeadlessJwtBearer</c> validates
/// against. Set them to the values the host passes to <see cref="IJwtTokenFactory"/> in <see cref="JwtTokenRequest"/>.
/// </summary>
[PublicAPI]
public sealed class HeadlessJwtBearerOptions
{
    /// <summary>
    /// The HMAC-SHA256 key that verifies the token signature, UTF-8 encoded. Must be at least 32 bytes, the same
    /// minimum <see cref="IJwtTokenFactory"/> enforces when it signs.
    /// </summary>
    public string SigningKey { get; set; } = "";

    /// <summary>
    /// The key that decrypts tokens the factory encrypted, UTF-8 encoded. Leave unset when tokens are signed only.
    /// </summary>
    public string? EncryptingKey { get; set; }

    /// <summary>The expected <c>iss</c> claim. Required when <see cref="ValidateIssuer"/> is <see langword="true"/>.</summary>
    public string? Issuer { get; set; }

    /// <summary>The expected <c>aud</c> claim. Required when <see cref="ValidateAudience"/> is <see langword="true"/>.</summary>
    public string? Audience { get; set; }

    /// <summary>Whether the issuer is validated. The default is <see langword="true"/>.</summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>Whether the audience is validated. The default is <see langword="true"/>.</summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Where the scheme reads the access token, tried in the order header, cookie, query string. The default is
    /// <see cref="JwtTokenSources.Header"/>, the <c>Authorization: Bearer</c> header only. Leaving
    /// <see cref="JwtTokenSources.Header"/> out makes the scheme ignore that header.
    /// </summary>
    public JwtTokenSources TokenSources { get; set; } = JwtTokenSources.Header;

    /// <summary>
    /// The cookie read when <see cref="TokenSources"/> includes <see cref="JwtTokenSources.Cookie"/>. The default is
    /// <c>__Host-access_token</c>. A cookie token is used only when the request also passes the same-origin guard
    /// that <c>HeadlessSameOriginOptions</c> configures, because a browser sends the cookie on cross-site requests too.
    /// </summary>
    public string CookieName { get; set; } = "__Host-access_token";

    /// <summary>
    /// The query-string parameter read when <see cref="TokenSources"/> includes
    /// <see cref="JwtTokenSources.QueryString"/>. The default is <c>access_token</c>, the name SignalR clients send.
    /// </summary>
    public string QueryParameterName { get; set; } = "access_token";

    /// <summary>
    /// The request path prefixes, such as <c>/hubs</c>, on which the query-string source is read; it is read nowhere
    /// else. A token in a URL lands in access logs and proxies, so limit it to the WebSocket and server-sent-event
    /// paths that cannot send a header. Required when <see cref="TokenSources"/> includes
    /// <see cref="JwtTokenSources.QueryString"/>. Each prefix starts with <c>/</c> and matches whole path segments.
    /// </summary>
    public List<string> QueryPathPrefixes { get; set; } = [];
}

/// <summary>Where a JWT bearer scheme registered by <c>AddHeadlessJwtBearer</c> reads the access token.</summary>
[Flags]
[PublicAPI]
public enum JwtTokenSources
{
    /// <summary>No source; not a valid setting.</summary>
    None = 0,

    /// <summary>The <c>Authorization: Bearer</c> request header.</summary>
    Header = 1,

    /// <summary>The cookie named by <see cref="HeadlessJwtBearerOptions.CookieName"/>.</summary>
    Cookie = 2,

    /// <summary>
    /// The query-string parameter named by <see cref="HeadlessJwtBearerOptions.QueryParameterName"/>, on the paths in
    /// <see cref="HeadlessJwtBearerOptions.QueryPathPrefixes"/> only.
    /// </summary>
    QueryString = 4,
}

internal sealed class HeadlessJwtBearerOptionsValidator : AbstractValidator<HeadlessJwtBearerOptions>
{
    public HeadlessJwtBearerOptionsValidator()
    {
        RuleFor(x => x.SigningKey)
            .Must(key => key is not null && Encoding.UTF8.GetByteCount(key) >= JwtTokenFactory.MinimumSigningKeyBytes)
            .WithMessage(
                $"SigningKey must be at least {JwtTokenFactory.MinimumSigningKeyBytes} bytes (256 bits) when UTF-8 encoded."
            );
        RuleFor(x => x.EncryptingKey).NotEmpty().When(x => x.EncryptingKey is not null);
        RuleFor(x => x.Issuer).NotEmpty().When(x => x.ValidateIssuer);
        RuleFor(x => x.Audience).NotEmpty().When(x => x.ValidateAudience);

        RuleFor(x => x.TokenSources)
            .Must(sources => sources != JwtTokenSources.None && (sources & ~_AllSources) == JwtTokenSources.None)
            .WithMessage("TokenSources must name at least one of Header, Cookie, and QueryString, and nothing else.");

        When(
            x => x.TokenSources.HasFlag(JwtTokenSources.Cookie),
            () =>
                RuleFor(x => x.CookieName)
                    .Must(HttpSyntax.IsToken)
                    .WithMessage(
                        "CookieName must be a valid cookie name: only letters, digits, and !#$%&'*+-.^_`|~ are allowed."
                    )
        );

        When(
            x => x.TokenSources.HasFlag(JwtTokenSources.QueryString),
            () =>
            {
                RuleFor(x => x.QueryParameterName).NotEmpty();
                RuleFor(x => x.QueryPathPrefixes)
                    .NotEmpty()
                    .WithMessage(
                        "QueryPathPrefixes needs at least one path, such as '/hubs', when TokenSources includes"
                            + " QueryString: a token in a URL leaks into logs, so it is read only where a header cannot be sent."
                    );
            }
        );

        RuleForEach(x => x.QueryPathPrefixes)
            .Must(prefix => prefix?.Length > 1 && prefix[0] == '/')
            .WithMessage((_, prefix) => $"Query path prefix '{prefix}' must start with '/' and name a path.");
    }

    private const JwtTokenSources _AllSources =
        JwtTokenSources.Header | JwtTokenSources.Cookie | JwtTokenSources.QueryString;
}
