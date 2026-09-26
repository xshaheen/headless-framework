// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.AspNetCore.Builder;

namespace Headless.Api.Idempotency;

[PublicAPI]
public static class IdempotencyApplicationBuilderExtensions
{
    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds the Stripe-style idempotency middleware to the pipeline.
        /// </summary>
        /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
        /// <remarks>
        /// Place <c>UseIdempotency()</c> AFTER <c>UseAuthorization()</c> and AFTER
        /// <c>UseHeadlessTenancy()</c>. The middleware scopes keys by the current user, and the durable
        /// store keys every record by the authenticated principal's tenant claim, never by the ambient
        /// tenant pre-auth resolution set from the host or a header. Anonymous requests share one
        /// namespace outside every tenant. Auth must be resolved first so unauthenticated and
        /// unauthorized requests don't allocate idempotency storage.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown at request time if <c>AddIdempotency()</c> was not called during service
        /// registration (required services are not in the DI container).
        /// </exception>
        public IApplicationBuilder UseIdempotency()
        {
            return app.UseMiddleware<IdempotencyMiddleware>();
        }
    }
}
