// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.BuildingBlocks;
using Microsoft.AspNetCore.Authentication;

namespace Framework.Api.Identity.Authentication.ApiKey;

[PublicAPI]
public sealed class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "API Key Authentication";
    public const string ParamName = "api_key";
    public const string HeaderName = HttpHeaderNames.ApiKey;

    public string ApiKeyParamName { get; set; } = ParamName;

    public string ApiKeyHeaderName { get; set; } = HeaderName;
}
