// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Http;

namespace Headless.Tus;

/// <summary>
/// One upload endpoint declared through <c>AddUploadEndpoint</c> inside <c>AddHeadlessTus</c>. Its limits are the
/// endpoint's <see cref="TusUploadEndpointOptions"/>, its members say who owns its uploads, and
/// <c>MapHeadlessTus</c> maps it to a route.
/// </summary>
[PublicAPI]
public sealed class HeadlessTusUploadEndpointBuilder
{
    internal HeadlessTusUploadEndpointBuilder(TusEndpointRegistration endpoint)
    {
        Endpoint = endpoint;
    }

    internal TusEndpointRegistration Endpoint { get; }

    /// <summary>
    /// Names the owner of the endpoint's uploads from the request, in place of the signed-in user.
    /// </summary>
    /// <param name="ownerKey">
    /// Returns the owner key of the request, for example a workspace id read from the route, or
    /// <see langword="null"/> when the request has none.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The key a creating request returns is recorded on the upload. Every later request for that upload must
    /// return the same key, compared by ordinal, or it is answered as not found, exactly like a request for an
    /// upload that does not exist. A request whose key is <see langword="null"/>, empty, or white space is refused
    /// with 401, and a creating request whose key is longer than 256 characters with 400.
    /// </para>
    /// <para>
    /// The endpoint trusts the key as returned: it does not check that the caller may act for that owner. Return
    /// a value the application has already authorized for the caller, for example behind an authorization policy
    /// on the mapped endpoint. The function runs on every request to the endpoint. Without it the owner is the
    /// signed-in user's id, read from <c>ICurrentUser</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ownerKey"/> is <see langword="null"/>.</exception>
    public HeadlessTusUploadEndpointBuilder OwnedBy(Func<HttpContext, string?> ownerKey)
    {
        Argument.IsNotNull(ownerKey);

        Endpoint.OwnerKey = context => ValueTask.FromResult(ownerKey(context));

        return this;
    }

    /// <inheritdoc cref="OwnedBy(Func{HttpContext, string})"/>
    /// <param name="ownerKey">
    /// Returns the owner key of the request, for example one looked up for the signed-in user, or
    /// <see langword="null"/> when the request has none.
    /// </param>
    public HeadlessTusUploadEndpointBuilder OwnedBy(Func<HttpContext, ValueTask<string?>> ownerKey)
    {
        Argument.IsNotNull(ownerKey);

        Endpoint.OwnerKey = ownerKey;

        return this;
    }
}
