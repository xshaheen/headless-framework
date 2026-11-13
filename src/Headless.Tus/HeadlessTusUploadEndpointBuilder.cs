// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus.Internal;

namespace Headless.Tus;

/// <summary>
/// One upload endpoint declared through <c>AddUploadEndpoint</c> inside <c>AddHeadlessTus</c>. Its limits are the
/// endpoint's <see cref="TusUploadEndpointOptions"/>; <c>MapHeadlessTus</c> maps it to a route.
/// </summary>
[PublicAPI]
public sealed class HeadlessTusUploadEndpointBuilder
{
    internal HeadlessTusUploadEndpointBuilder(TusEndpointRegistration endpoint)
    {
        Endpoint = endpoint;
    }

    internal TusEndpointRegistration Endpoint { get; }
}
