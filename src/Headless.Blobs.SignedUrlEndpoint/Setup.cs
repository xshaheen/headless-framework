// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Blobs;

/// <summary>Registers the signed-URL endpoint for blob stores without native presign.</summary>
[PublicAPI]
public static class SetupBlobSignedUrl
{
    extension(HeadlessBlobsSetupBuilder setup)
    {
        /// <summary>
        /// Gives every default and named store that lacks native presign an <see cref="IPresignedUrlBlobStorage"/>
        /// capability whose URLs point at the endpoint <c>MapBlobSignedUrlEndpoint</c> maps. Stores with native
        /// presign (AWS, Azure, Cloudflare R2) keep it and are not wrapped.
        /// </summary>
        /// <param name="setupAction">Configures the endpoint's public base URL and route prefix.</param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// The tokens are ASP.NET Core data-protection payloads, so every replica that mints or serves them must share
        /// one persisted key ring, and a URL outlives no key it was protected with. A store registered as an instance
        /// becomes container-disposed once it is decorated.
        /// </remarks>
        public HeadlessBlobsSetupBuilder UseSignedUrlEndpoint(Action<BlobSignedUrlOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterCrossCuttingExtension(services =>
            {
                services.Configure<BlobSignedUrlOptions, BlobSignedUrlOptionsValidator>(setupAction);
                services.AddDataProtection();
                services.TryAddSingleton(TimeProvider.System);
                services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
                services.TryAddSingleton<BlobSignedUrlSigner>();

                // Cross-cutting extensions run after every provider contribution, so every store is registered by now.
                services.TryDecorate<IBlobStorage>((inner, provider) => _Wrap(inner, store: null, provider));

                var storeNames = services
                    .Where(static d => d.IsKeyedService && d.ServiceType == typeof(IBlobStorage))
                    .Select(static d => d.ServiceKey)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                foreach (var name in storeNames)
                {
                    services.TryDecorateKeyed<IBlobStorage>(name, (inner, provider) => _Wrap(inner, name, provider));

                    // AWS, Azure, and R2 already register this forward; the rest gain it through the wrapper.
                    services.TryAddKeyedSingleton<IPresignedUrlBlobStorage>(
                        name,
                        (provider, _) => (IPresignedUrlBlobStorage)provider.GetRequiredKeyedService<IBlobStorage>(name)
                    );
                }
            });

            return setup;
        }
    }

    private static IBlobStorage _Wrap(IBlobStorage inner, string? store, IServiceProvider provider)
    {
        return inner is IPresignedUrlBlobStorage
            ? inner
            : new SignedUrlBlobStorage(inner, store, provider.GetRequiredService<BlobSignedUrlSigner>());
    }
}
