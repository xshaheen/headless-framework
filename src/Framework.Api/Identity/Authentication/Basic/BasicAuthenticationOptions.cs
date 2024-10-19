// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.AspNetCore.Authentication;

namespace Framework.Api.Identity.Authentication.Basic;

/// <summary>
/// Option class provides information needed to control Basic Authentication handler behavior
/// </summary>
[PublicAPI]
public sealed class BasicAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "Basic Authentication";

    public string Scheme { get; set; } = DefaultScheme;
}
