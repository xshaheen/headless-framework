// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;

namespace Framework.BuildingBlocks.Helpers.Network;

public sealed class BasicAuthenticationValue : AuthenticationHeaderValue
{
    public const string BasicScheme = "Basic";

    public BasicAuthenticationValue()
        : base(BasicScheme) { }

    public BasicAuthenticationValue(string userName, string password)
        : base(BasicScheme, parameter: _ToBase64String($"{userName}:{password}")) { }

    public BasicAuthenticationValue(string value)
        : base(BasicScheme, parameter: _ToBase64String(value)) { }

    private static string _ToBase64String(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value), Base64FormattingOptions.None);
    }
}
