// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Security;

/// <summary>Configures the cookie <see cref="IRefreshTokenCookie"/> writes.</summary>
/// <remarks>
/// <c>Secure</c> and <c>HttpOnly</c> are always set and have no option: a refresh token must never travel over plain
/// HTTP or be readable from script.
/// </remarks>
[PublicAPI]
public sealed class RefreshTokenCookieOptions
{
    /// <summary>The prefix that locks a cookie to the exact host that set it, with path <c>/</c> and no domain.</summary>
    public const string HostPrefix = "__Host-";

    /// <summary>
    /// The cookie name. The default is <c>__Host-refresh_token</c>; the <c>__Host-</c> prefix makes the browser
    /// refuse the cookie unless it has <see cref="Path"/> <c>/</c> and no <see cref="Domain"/>, so no other subdomain
    /// can overwrite it. To narrow <see cref="Path"/> to the refresh endpoint, drop the prefix or use <c>__Secure-</c>.
    /// </summary>
    public string Name { get; set; } = HostPrefix + "refresh_token";

    /// <summary>The cookie path. Starts with <c>/</c>. The default is <c>/</c>.</summary>
    public string Path { get; set; } = "/";

    /// <summary>
    /// The cookie domain. The default, <see langword="null"/>, sends the cookie only to the host that set it. Must stay
    /// unset with a <c>__Host-</c> name.
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// The cookie's <c>SameSite</c> attribute: <see cref="SameSiteMode.Strict"/>, the default, or
    /// <see cref="SameSiteMode.Lax"/>. <see cref="SameSiteMode.None"/> is refused, because it exists for cross-site use,
    /// which the same-origin guard on refresh and logout endpoints rejects anyway.
    /// </summary>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;
}

internal sealed class RefreshTokenCookieOptionsValidator : AbstractValidator<RefreshTokenCookieOptions>
{
    public RefreshTokenCookieOptionsValidator()
    {
        RuleFor(x => x.Name)
            .Must(HttpSyntax.IsToken)
            .WithMessage(
                "The refresh-token cookie Name must be a valid cookie name: only letters, digits, and"
                    + " !#$%&'*+-.^_`|~ are allowed."
            );

        RuleFor(x => x.Path)
            .Must(path => path?.StartsWith('/') == true)
            .WithMessage("The refresh-token cookie Path must start with '/'.");

        RuleFor(x => x.Domain).NotEmpty().When(x => x.Domain is not null);

        // Browsers drop a __Host- cookie that has another path or any domain, so the write would silently do nothing.
        When(
            x => x.Name?.StartsWith(RefreshTokenCookieOptions.HostPrefix, StringComparison.Ordinal) == true,
            () =>
            {
                RuleFor(x => x.Path).Equal("/").WithMessage("A cookie named with the __Host- prefix needs Path '/'.");
                RuleFor(x => x.Domain)
                    .Null()
                    .WithMessage("A cookie named with the __Host- prefix must not set a Domain.");
            }
        );

        RuleFor(x => x.SameSite)
            .Must(mode => mode is SameSiteMode.Strict or SameSiteMode.Lax)
            .WithMessage("The refresh-token cookie SameSite must be Strict or Lax.");
    }
}
