// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;

namespace Framework.Http;

public sealed class BasicAuthenticationValue : AuthenticationHeaderValue
{
    public const string BasicScheme = "Basic";

    public BasicAuthenticationValue()
        : base(BasicScheme) { }

    public BasicAuthenticationValue(string userName, string password)
        : base(BasicScheme, parameter: $"{userName}:{password}".ToBase64()) { }

    public BasicAuthenticationValue(string value)
        : base(BasicScheme, parameter: value.ToBase64()) { }
}
