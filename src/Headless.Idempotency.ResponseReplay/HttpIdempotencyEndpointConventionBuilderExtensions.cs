// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;

namespace Headless.Idempotency;

[PublicAPI]
public static class HttpIdempotencyEndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Attaches per-endpoint idempotency option overrides. The configure delegate runs
        /// once per request against a fresh clone of the application-level options.
        /// </summary>
        /// <param name="configure">
        /// Delegate applied to a per-request clone of <see cref="HttpIdempotencyOptions"/>. May
        /// mutate <see cref="HttpIdempotencyOptions.Methods"/>,
        /// <see cref="HttpIdempotencyOptions.ReplayHeaderAllowlist"/>, scalar settings, and
        /// delegate properties. Changes are scoped to the current request and do not affect
        /// other endpoints or other in-flight requests.
        /// </param>
        /// <returns>The endpoint convention builder for further chaining.</returns>
        /// <remarks>
        /// <see cref="HttpIdempotencyOptions.HeaderName"/> overrides are ignored — the middleware
        /// reads the header before resolving endpoint metadata. To change the header for a
        /// single endpoint, pre-set it via custom middleware. All other options
        /// (<see cref="HttpIdempotencyOptions.Methods"/>,
        /// <see cref="HttpIdempotencyOptions.ReplayHeaderAllowlist"/>, delegates, scalars) are
        /// merged as expected.
        /// <para>
        /// The endpoint also gets <see cref="Headless.Api.HttpContractMetadata"/> describing the key header and the
        /// responses the middleware can write for it, which Headless.OpenApi renders. It reflects the application-level
        /// options and every override the endpoint declared, read once when its conventions run.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
        public TBuilder WithIdempotency(Action<HttpIdempotencyOptions> configure)
        {
            builder.WithMetadata(new HttpIdempotencyMetadata(configure));
            builder.Add(HttpIdempotencyContract.Attach);

            return builder;
        }

        /// <summary>
        /// Refuses a request to this endpoint that carries no idempotency key with <c>400</c>
        /// (<see cref="HttpIdempotencyErrorCodes.KeyRequired" />), so a client can never run the operation without the
        /// protection against duplicates. Combines with <c>WithIdempotency</c> in either order.
        /// </summary>
        /// <remarks>
        /// The endpoint also gets <see cref="Headless.Api.HttpContractMetadata"/> describing the now required key header
        /// and the responses the middleware can write for it, which Headless.OpenApi renders.
        /// </remarks>
        /// <returns>The builder.</returns>
        public TBuilder RequireIdempotencyKey()
        {
            builder.WithMetadata(new HttpIdempotencyMetadata(static options => options.KeyRequired = true));
            builder.Add(HttpIdempotencyContract.Attach);

            return builder;
        }
    }
}
