// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Implemented by the options of an authentication scheme to describe where the scheme reads the client's credential,
/// so API documentation can publish the scheme without depending on the package that implements its handler.
/// </summary>
/// <remarks>
/// Headless.OpenApi reads it from the named options of every registered authentication scheme and publishes one
/// security scheme per credential. The API-key and Basic schemes of <c>Headless.Api.Identity</c> implement it.
/// </remarks>
[PublicAPI]
public interface IHttpCredentialSchemeOptions
{
    /// <summary>
    /// The credentials the scheme accepts under its current configuration, in the order the handler reads them. Empty
    /// when the scheme reads no credential from the request.
    /// </summary>
    IReadOnlyList<HttpCredentialDescription> Credentials { get; }
}
