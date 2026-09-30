// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

/// <summary>Maps the signed-URL endpoint registered by <c>UseSignedUrlEndpoint</c>.</summary>
[PublicAPI]
public static class BlobSignedUrlEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps <c>GET</c> (download) and <c>PUT</c> (upload) at <c>{RoutePrefix}/{token}</c> for the URLs minted by
        /// stores wrapped through <c>UseSignedUrlEndpoint</c>.
        /// </summary>
        /// <returns>The route group, to attach rate limiting, CORS, or other conventions.</returns>
        /// <remarks>
        /// The endpoints allow anonymous access because the token is the credential. An expired, tampered, wrong-verb,
        /// or unknown-store token and a missing blob all return 404, so a probe learns nothing about what exists.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <c>UseSignedUrlEndpoint</c> was not called inside <c>AddHeadlessBlobs</c>.
        /// </exception>
        public RouteGroupBuilder MapBlobSignedUrlEndpoint()
        {
            if (endpoints.ServiceProvider.GetService<BlobSignedUrlSigner>() is null)
            {
                throw new InvalidOperationException(
                    "The blob signed-URL endpoint is not registered. Call "
                        + "services.AddHeadlessBlobs(setup => setup.UseSignedUrlEndpoint(…)) before mapping it."
                );
            }

            var options = endpoints.ServiceProvider.GetRequiredService<IOptions<BlobSignedUrlOptions>>().Value;
            var group = endpoints.MapGroup(options.RoutePrefix.TrimEnd('/')).AllowAnonymous().ExcludeFromDescription();

            group.MapGet("/{token}", BlobSignedUrlEndpoint.DownloadAsync);
            group.MapPut("/{token}", BlobSignedUrlEndpoint.UploadAsync);

            return group;
        }
    }
}
