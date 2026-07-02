// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>DI registration helpers for the tus expired-uploads cleanup job.</summary>
[PublicAPI]
public static class SetupTusExpiredUploadsCleanup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="TusExpiredUploadsCleanupService"/>, a background job that
        /// periodically removes expired <em>incomplete</em> uploads.
        /// </summary>
        /// <param name="setupAction">optional configuration; the default runs every 5 minutes</param>
        /// <returns>the same service collection for chaining</returns>
        /// <remarks>
        /// Requires an <see cref="ITusExpirationStore"/> registration. Store packages register the
        /// forwarding automatically (for example <c>AddTusAzureStore</c>); a manually constructed
        /// store can be registered with
        /// <c>services.AddSingleton&lt;ITusExpirationStore&gt;(store)</c>. Expiration itself is
        /// configured on <c>DefaultTusConfiguration.Expiration</c> — without it uploads never
        /// expire and this job removes nothing.
        /// </remarks>
        public IServiceCollection AddTusExpiredUploadsCleanup(
            Action<TusExpiredUploadsCleanupOptions>? setupAction = null
        )
        {
            services.Configure<TusExpiredUploadsCleanupOptions, TusExpiredUploadsCleanupOptionsValidator>(setupAction);
            services.TryAddSingleton(TimeProvider.System);
            services.AddHostedService<TusExpiredUploadsCleanupService>();

            return services;
        }
    }
}
