// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// Well-known authentication identifiers used across the framework: the default
/// <c>AuthenticationType</c> stamped on issued <see cref="System.Security.Claims.ClaimsIdentity"/>
/// instances, and the canonical authentication-scheme names that match ASP.NET Core Identity's
/// built-in scheme registrations.
/// </summary>
public static class AuthenticationConstants
{
    /// <summary>
    /// The <c>AuthenticationType</c> assigned to an authenticated <see cref="System.Security.Claims.ClaimsIdentity"/>.
    /// A non-empty value is required for <see cref="System.Security.Claims.ClaimsIdentity.IsAuthenticated"/> to be <see langword="true"/>.
    /// </summary>
    public const string IdentityAuthenticationType = "AuthenticationTypes.Federation";

    /// <summary>
    /// Canonical authentication-scheme names. The values intentionally mirror the scheme names
    /// registered by ASP.NET Core (Identity / OpenIddict / JWT bearer), so a property name and its
    /// underlying string may differ (for example <see cref="OpenId"/> is <c>"OIDC"</c> and the
    /// Identity schemes use the <c>"Identity.*"</c> prefix).
    /// </summary>
    public static class Schemas
    {
        /// <summary>OpenID Connect authentication scheme (<c>OIDC</c>).</summary>
        public const string OpenId = "OIDC";

        /// <summary>JWT bearer authentication scheme (<c>Bearer</c>).</summary>
        public const string Bearer = "Bearer";

        /// <summary>HTTP Basic authentication scheme (<c>Basic</c>).</summary>
        public const string Basic = "Basic";

        /// <summary>API-key authentication scheme (<c>ApiKey</c>).</summary>
        public const string ApiKey = "ApiKey";

        /// <summary>ASP.NET Core Identity's primary application cookie scheme (<c>IdentityConstants.ApplicationScheme</c>).</summary>
        public const string Cookie = "Identity.Application";

        /// <summary>ASP.NET Core Identity's external sign-in cookie scheme (<c>IdentityConstants.ExternalScheme</c>).</summary>
        public const string External = "Identity.External";

        /// <summary>ASP.NET Core Identity's "remember this machine" two-factor cookie scheme (<c>IdentityConstants.TwoFactorRememberMeScheme</c>).</summary>
        public const string TwoFactorRememberMe = "Identity.TwoFactorRememberMe";

        /// <summary>ASP.NET Core Identity's intermediate two-factor user-id cookie scheme (<c>IdentityConstants.TwoFactorUserIdScheme</c>).</summary>
        public const string TwoFactorUserId = "Identity.TwoFactorUserId";
    }
}
