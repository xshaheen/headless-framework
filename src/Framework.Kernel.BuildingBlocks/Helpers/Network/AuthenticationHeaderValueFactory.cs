// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Text;
using Framework.Kernel.Checks;

namespace Framework.Kernel.BuildingBlocks.Helpers.Network;

public static class AuthenticationHeaderValueFactory
{
    public const string BasicScheme = "Basic";

    public static AuthenticationHeaderValue CreateBasic(string userName, string password)
    {
        Argument.IsNotNullOrWhiteSpace(userName);
        Argument.IsNotNull(password);

        var encodedCredential = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{userName}:{password}"),
            Base64FormattingOptions.None
        );

        return new(BasicScheme, encodedCredential);
    }
}
