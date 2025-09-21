// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Framework.Checks;

namespace Framework.Http;

public static class AuthenticationHeaderFactory
{
    public static AuthenticationHeaderValue CreateBasic(string userName, string password)
    {
        Argument.IsNotNullOrWhiteSpace(userName);
        Argument.IsNotNull(password);

        return new(BasicAuthenticationValue.BasicScheme, $"{userName}:{password}".ToBase64());
    }

    public static AuthenticationHeaderValue CreateBasic(string value)
    {
        Argument.IsNotNullOrWhiteSpace(value);

        return new(BasicAuthenticationValue.BasicScheme, value.ToBase64());
    }

    public static AuthenticationHeaderValue CreateBasic(ReadOnlySpan<byte> value)
    {
        return new(BasicAuthenticationValue.BasicScheme, value.ToBase64());
    }
}
