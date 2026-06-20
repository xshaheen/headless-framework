// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;

namespace Headless.Http;

/// <summary>An <see cref="AuthenticationHeaderValue"/> specialized for the HTTP <c>Basic</c> authentication scheme.</summary>
public sealed class BasicAuthenticationValue : AuthenticationHeaderValue
{
    /// <summary>The HTTP authentication scheme name (<c>Basic</c>) used by this header value.</summary>
    public const string BasicScheme = "Basic";

    /// <summary>Initializes a <c>Basic</c> authentication header with no credential parameter.</summary>
    public BasicAuthenticationValue()
        : base(BasicScheme) { }

    /// <summary>Initializes a <c>Basic</c> authentication header from a Base64-encoded <c>userName:password</c> pair.</summary>
    /// <param name="userName">The user name credential.</param>
    /// <param name="password">The password credential.</param>
    public BasicAuthenticationValue(string userName, string password)
        : base(BasicScheme, parameter: $"{userName}:{password}".ToBase64()) { }

    /// <summary>Initializes a <c>Basic</c> authentication header by Base64-encoding an already-formatted credential value.</summary>
    /// <param name="value">The raw <c>userName:password</c> credential string to encode.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public BasicAuthenticationValue(string value)
        : base(BasicScheme, parameter: value.ToBase64()) { }
}
