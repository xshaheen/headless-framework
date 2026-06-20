// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.Http;

/// <summary>Factory helpers for building HTTP <see cref="AuthenticationHeaderValue"/> instances.</summary>
[PublicAPI]
public static class AuthenticationHeaderFactory
{
    /// <summary>Builds a <c>Basic</c> authentication header by Base64-encoding the <c>userName:password</c> pair.</summary>
    /// <param name="userName">The user name credential.</param>
    /// <param name="password">The password credential.</param>
    /// <returns>An <see cref="AuthenticationHeaderValue"/> using the <c>Basic</c> scheme.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="userName"/> or <paramref name="password"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="userName"/> is empty or white space.</exception>
    public static AuthenticationHeaderValue CreateBasic(string userName, string password)
    {
        Argument.IsNotNullOrWhiteSpace(userName);
        Argument.IsNotNull(password);

        return new(BasicAuthenticationValue.BasicScheme, $"{userName}:{password}".ToBase64());
    }

    /// <summary>Builds a <c>Basic</c> authentication header by Base64-encoding an already-formatted <c>userName:password</c> value.</summary>
    /// <param name="value">The raw <c>userName:password</c> credential string to encode.</param>
    /// <returns>An <see cref="AuthenticationHeaderValue"/> using the <c>Basic</c> scheme.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty or white space.</exception>
    public static AuthenticationHeaderValue CreateBasic(string value)
    {
        Argument.IsNotNullOrWhiteSpace(value);

        return new(BasicAuthenticationValue.BasicScheme, value.ToBase64());
    }

    /// <summary>Builds a <c>Basic</c> authentication header by Base64-encoding the supplied credential bytes.</summary>
    /// <param name="value">The raw credential bytes (typically the UTF-8 bytes of <c>userName:password</c>) to encode.</param>
    /// <returns>An <see cref="AuthenticationHeaderValue"/> using the <c>Basic</c> scheme.</returns>
    public static AuthenticationHeaderValue CreateBasic(ReadOnlySpan<byte> value)
    {
        return new(BasicAuthenticationValue.BasicScheme, value.ToBase64());
    }
}
