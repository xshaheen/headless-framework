// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        /// <c>UseHeadlessTenancy()</c>. The middleware reads <c>ICurrentTenant.Id</c> for
        /// cache-key composition; tenant and auth must be resolved first so unauthenticated
        /// and unauthorized requests don't allocate idempotency storage.
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
