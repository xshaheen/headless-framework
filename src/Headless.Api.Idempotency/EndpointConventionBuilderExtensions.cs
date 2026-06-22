// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

[PublicAPI]
public static class IdempotencyEndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Attaches per-endpoint idempotency option overrides. The configure delegate runs
        /// once per request against a fresh clone of the application-level options.
        /// </summary>
        /// <param name="configure">
        /// Delegate applied to a per-request clone of <see cref="IdempotencyOptions"/>. May
        /// mutate <see cref="IdempotencyOptions.Methods"/>,
        /// <see cref="IdempotencyOptions.ReplayHeaderAllowlist"/>, scalar settings, and
        /// delegate properties. Changes are scoped to the current request and do not affect
        /// other endpoints or other in-flight requests.
        /// </param>
        /// <returns>The endpoint convention builder for further chaining.</returns>
        /// <remarks>
        /// <see cref="IdempotencyOptions.HeaderName"/> overrides are ignored — the middleware
        /// reads the header before resolving endpoint metadata. To change the header for a
        /// single endpoint, pre-set it via custom middleware. All other options
        /// (<see cref="IdempotencyOptions.Methods"/>,
        /// <see cref="IdempotencyOptions.ReplayHeaderAllowlist"/>, delegates, scalars) are
        /// merged as expected.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
        public TBuilder WithIdempotency(Action<IdempotencyOptions> configure)
        {
            return builder.WithMetadata(new IdempotencyMetadata(configure));
        }
    }
}
