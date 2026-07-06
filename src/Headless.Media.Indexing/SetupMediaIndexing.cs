// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Media.Indexing;

/// <summary>
/// Registration extensions for the Headless media text-indexing providers.
/// </summary>
[PublicAPI]
public static class SetupMediaIndexing
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the three built-in text providers (PDF, Word, PowerPoint) — each as its concrete
        /// type and as an <see cref="IMediaFileTextProvider"/> enumerable entry — plus the
        /// <see cref="IMediaFileTextProviderResolver"/> that dispatches by file extension or MIME type.
        /// </summary>
        /// <remarks>
        /// All registrations use <c>TryAdd</c> / <c>TryAddEnumerable</c>, so the method is idempotent and
        /// a consumer can override any provider by registering their own beforehand. Providers are
        /// stateless and registered as singletons.
        /// </remarks>
        /// <returns>The same <paramref name="services"/> collection for chaining.</returns>
        public IServiceCollection AddMediaIndexing()
        {
            services.TryAddSingleton<PdfMediaFileTextProvider>();
            services.TryAddSingleton<WordDocumentMediaFileTextProvider>();
            services.TryAddSingleton<PresentationDocumentMediaFileTextProvider>();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IMediaFileTextProvider, PdfMediaFileTextProvider>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IMediaFileTextProvider, WordDocumentMediaFileTextProvider>()
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IMediaFileTextProvider, PresentationDocumentMediaFileTextProvider>()
            );

            services.TryAddSingleton<IMediaFileTextProviderResolver, MediaFileTextProviderResolver>();

            return services;
        }
    }
}
