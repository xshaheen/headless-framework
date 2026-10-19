// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;
using Microsoft.AspNetCore.Authentication;

namespace Headless.Api.Identity;

/// <summary>Options for the HTTP Basic authentication scheme.</summary>
[PublicAPI]
public sealed class BasicAuthenticationOptions : AuthenticationSchemeOptions, IHttpCredentialSchemeOptions
{
    /// <summary>Default scheme name used when none is specified during registration.</summary>
    public const string DefaultScheme = AuthenticationConstants.Schemas.Basic;

    /// <summary>
    /// Scheme name included in the <see cref="Microsoft.AspNetCore.Authentication.AuthenticationTicket"/>.
    /// Defaults to <see cref="DefaultScheme"/>.
    /// </summary>
    public string Scheme { get; set; } = DefaultScheme;

    /// <inheritdoc/>
    /// <remarks>The <c>Authorization</c> header under the <c>Basic</c> scheme (RFC 7617).</remarks>
    public IReadOnlyList<HttpCredentialDescription> Credentials { get; } =
    [
        HttpCredentialDescription.Authorization(
            "Basic",
            "The user name and password, joined by a colon and Base64-encoded, in the Authorization header."
        ),
    ];
}
