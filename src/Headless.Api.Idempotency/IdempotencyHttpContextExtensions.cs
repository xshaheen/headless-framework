// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

[PublicAPI]
public static class HeadlessIdempotencyHttpContextExtensions
{
    /// <summary>Returns the idempotency admission the current request reached its handler under.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>
    /// The admission, or <see langword="null" /> when the request carried no idempotency key, idempotency did not apply
    /// to it, or the idempotency middleware is not in the pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is <see langword="null" />.</exception>
    public static IIdempotencyContext? GetIdempotencyContext(this HttpContext context)
    {
        Argument.IsNotNull(context);

        return context.Features.Get<IIdempotencyContext>();
    }
}
