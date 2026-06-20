// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authentication;

namespace Headless.Api.Identity.Authentication.Basic;

/// <summary>Options for the HTTP Basic authentication scheme.</summary>
[PublicAPI]
public sealed class BasicAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>Default scheme name used when none is specified during registration.</summary>
    public const string DefaultScheme = "Basic Authentication";

    /// <summary>
    /// Scheme name included in the <see cref="Microsoft.AspNetCore.Authentication.AuthenticationTicket"/>.
    /// Defaults to <see cref="DefaultScheme"/>.
    /// </summary>
    public string Scheme { get; set; } = DefaultScheme;
}
