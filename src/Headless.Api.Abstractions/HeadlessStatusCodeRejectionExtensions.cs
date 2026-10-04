// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Checks;

namespace Microsoft.AspNetCore.Http;

[PublicAPI]
public static class HeadlessStatusCodeRejectionExtensions
{
    /// <summary>
    /// Stores <paramref name="rejection"/> as the request's <see cref="IStatusCodeRejectionFeature"/>
    /// unless another handler already set one, so the first failure keeps ownership of the response.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="rejection">The rejection to write when the status-codes rewriter runs.</param>
    /// <returns><see langword="true"/> when stored; <see langword="false"/> when a rejection was already present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="rejection"/> is <see langword="null"/>.</exception>
    public static bool TrySetStatusCodeRejection(this HttpContext context, IStatusCodeRejectionFeature rejection)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(rejection);

        if (context.Features.Get<IStatusCodeRejectionFeature>() is not null)
        {
            return false;
        }

        context.Features.Set(rejection);

        return true;
    }
}
