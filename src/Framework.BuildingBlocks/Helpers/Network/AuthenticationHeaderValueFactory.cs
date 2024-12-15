// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Framework.Checks;

namespace Framework.BuildingBlocks.Helpers.Network;

public static class AuthenticationHeaderValueFactory
{
    public static AuthenticationHeaderValue CreateBasic(string userName, string password)
    {
        Argument.IsNotNullOrWhiteSpace(userName);
        Argument.IsNotNull(password);

        var encodedCredential = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{userName}:{password}"),
            Base64FormattingOptions.None
        );

        return new(BasicAuthenticationValue.BasicScheme, encodedCredential);
    }

    public static AuthenticationHeaderValue CreateBasic(string value)
    {
        Argument.IsNotNullOrWhiteSpace(value);

        var encodedCredential = Convert.ToBase64String(Encoding.UTF8.GetBytes(value), Base64FormattingOptions.None);

        return new(BasicAuthenticationValue.BasicScheme, encodedCredential);
    }

    public static AuthenticationHeaderValue CreateBasic(ReadOnlySpan<byte> value)
    {
        var encodedCredential = Convert.ToBase64String(value);

        return new(BasicAuthenticationValue.BasicScheme, encodedCredential);
    }
}
