// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Constants;

public static class AuthenticationConstants
{
    public const string IdentityAuthenticationType = "AuthenticationTypes.Federation";

    public static class Schemas
    {
        public const string OpenId = "OIDC";
        public const string Bearer = "Bearer";
        public const string Basic = "Basic";
        public const string ApiKey = "ApiKey";
        public const string Cookie = "Identity.Application";
        public const string External = "Identity.External";
        public const string TwoFactorRememberMe = "Identity.TwoFactorRememberMe";
        public const string TwoFactorUserId = "Identity.TwoFactorUserId";
    }
}
