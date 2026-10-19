// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Api;

/// <summary>A credential an authentication scheme reads from the request, as part of <see cref="IHttpCredentialSchemeOptions"/>.</summary>
[PublicAPI]
public sealed record HttpCredentialDescription
{
    private HttpCredentialDescription(HttpCredentialLocation location, string name, string description)
    {
        Location = location;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        Description = Argument.IsNotNullOrWhiteSpace(description);
    }

    /// <summary>Where the request carries the credential.</summary>
    public HttpCredentialLocation Location { get; }

    /// <summary>
    /// The header or query parameter name, or for <see cref="HttpCredentialLocation.Authorization"/> the authentication
    /// scheme of the <c>Authorization</c> header, such as <c>Basic</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>What the credential is and how to send it.</summary>
    public string Description { get; }

    /// <summary>Describes a credential carried in a request header.</summary>
    /// <param name="name">The header name. Must not be empty or white space.</param>
    /// <param name="description">What the credential is and how to send it. Must not be empty or white space.</param>
    /// <returns>The credential description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="description"/> is empty or white space.</exception>
    public static HttpCredentialDescription Header(string name, string description)
    {
        return new(HttpCredentialLocation.Header, name, description);
    }

    /// <summary>Describes a credential carried in a query string parameter.</summary>
    /// <param name="name">The query parameter name. Must not be empty or white space.</param>
    /// <param name="description">What the credential is and how to send it. Must not be empty or white space.</param>
    /// <returns>The credential description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="description"/> is empty or white space.</exception>
    public static HttpCredentialDescription Query(string name, string description)
    {
        return new(HttpCredentialLocation.Query, name, description);
    }

    /// <summary>Describes a credential carried in the <c>Authorization</c> header under an HTTP authentication scheme.</summary>
    /// <param name="scheme">The HTTP authentication scheme, such as <c>Basic</c> or <c>Bearer</c>. Must not be empty or white space.</param>
    /// <param name="description">What the credential is and how to send it. Must not be empty or white space.</param>
    /// <returns>The credential description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scheme"/> or <paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="scheme"/> or <paramref name="description"/> is empty or white space.</exception>
    public static HttpCredentialDescription Authorization(string scheme, string description)
    {
        return new(HttpCredentialLocation.Authorization, scheme, description);
    }
}

/// <summary>Where a request carries the credential of an <see cref="HttpCredentialDescription"/>.</summary>
[PublicAPI]
public enum HttpCredentialLocation
{
    /// <summary>A named request header.</summary>
    Header = 0,

    /// <summary>A named query string parameter.</summary>
    Query = 1,

    /// <summary>The <c>Authorization</c> header, under the HTTP authentication scheme the description names.</summary>
    Authorization = 2,
}
