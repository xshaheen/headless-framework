// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
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
        /// <remarks>
        /// <c>HeaderName</c> overrides are ignored — the middleware reads the header before
        /// resolving endpoint metadata. To change the header for a single endpoint, pre-set
        /// it via custom middleware. Other options (Methods, ReplayHeaderAllowlist, delegates,
        /// scalars) are merged as expected.
        /// </remarks>
        public TBuilder WithIdempotency(Action<IdempotencyOptions> configure)
        {
            return builder.WithMetadata(new IdempotencyMetadata(configure));
        }
    }
}
