// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

[PublicAPI]
public static class IdempotencyApplicationBuilderExtensions
{
    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds the Stripe-style idempotency middleware to the pipeline.
        /// </summary>
        /// <remarks>
        /// Place <c>UseIdempotency()</c> AFTER <c>UseAuthorization()</c> and AFTER
        /// <c>UseHeadlessTenancy()</c>. The middleware reads <c>ICurrentTenant.Id</c> for
        /// cache-key composition; tenant and auth must be resolved first so unauthenticated
        /// and unauthorized requests don't allocate idempotency storage.
        /// </remarks>
        public IApplicationBuilder UseIdempotency()
        {
            return app.UseMiddleware<IdempotencyMiddleware>();
        }
    }
}
