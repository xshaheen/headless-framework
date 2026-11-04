// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
        /// <para>
        /// The token in a URL's path is replaced by <c>***</c> in HTTP logging (<c>UseHttpLogging</c>), in the
        /// request activity's <c>url.path</c>, <c>http.target</c>, and <c>url.full</c> tags, and in ASP.NET Core's own
        /// request scope (<c>RequestPath</c>) and its "Request starting" and "Request finished" lines. The hosting logs
        /// are rewritten by decorating the registered <see cref="ILoggerFactory"/>, so a logger factory registered
        /// after this call, such as Serilog's <c>UseSerilog</c>, replaces the decorator; the host then logs a warning
        /// at startup. Call this after the logger factory is registered.
        /// </para>
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

                // The token is a live credential, so its path stays out of HTTP logging and traces whenever the
                // endpoint exists. Without HTTP logging or an activity listener, neither hook changes anything, and
                // a request outside the route prefix costs one path comparison.
                services.TryAddSingleton<BlobSignedUrlPathRedactor>();
                services.AddHttpLoggingInterceptor<BlobSignedUrlHttpLoggingInterceptor>();

                // ASP.NET Core opens its request scope and writes its request lines before any middleware, through a
                // logger it takes from this factory when the server starts.
                if (!services.Any(static d => d.ServiceType == typeof(BlobSignedUrlLogRedactionMarker)))
                {
                    services.AddSingleton<BlobSignedUrlLogRedactionMarker>();
                    services.TryDecorateWith<ILoggerFactory>(
                        static (inner, provider) =>
                            new BlobSignedUrlRedactingLoggerFactory(
                                inner,
                                provider.GetRequiredService<BlobSignedUrlPathRedactor>()
                            )
                    );
                }

                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IStartupFilter, BlobSignedUrlTraceRedactionStartupFilter>()
                );

                // Cross-cutting extensions run after every provider contribution, so every store is registered by now.
                services.TryDecorateWith<IBlobStorage>((inner, provider) => _Wrap(inner, store: null, provider));

                var storeNames = services
                    .Where(static d => d.IsKeyedService && d.ServiceType == typeof(IBlobStorage))
                    .Select(static d => d.ServiceKey)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                foreach (var name in storeNames)
                {
                    services.TryDecorateKeyedWith<IBlobStorage>(
                        name,
                        (inner, provider) => _Wrap(inner, name, provider)
                    );

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

    /// <summary>Records that the logger factory was decorated, so a second registration does not wrap it twice.</summary>
    private sealed class BlobSignedUrlLogRedactionMarker;

    private static IBlobStorage _Wrap(IBlobStorage inner, string? store, IServiceProvider provider)
    {
        return inner is IPresignedUrlBlobStorage
            ? inner
            : new SignedUrlBlobStorage(inner, store, provider.GetRequiredService<BlobSignedUrlSigner>());
    }
}
