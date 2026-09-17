// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Adapts a <c>Func&lt;HttpContext, string?&gt;</c> resolver into an <see cref="ITenantIdentifierSource"/>,
/// registered by <c>AddSource(Func&lt;HttpContext, string?&gt;)</c> (R4).
/// </summary>
/// <remarks>
/// A <see langword="null"/> or blank return maps to <see cref="TenantIdentifierSourceResult.None"/> so
/// resolution continues with later sources; a delegate cannot express
/// <see cref="TenantIdentifierSourceResult.Invalid"/> — an ambiguous input needs the full three-state
/// result, which is what implementing the interface is for. Exceptions from <paramref name="resolver"/>
/// propagate unchanged: like a store fault, they are never mapped to a tenant outcome.
/// </remarks>
internal sealed class DelegateTenantIdentifierSource(Func<HttpContext, string?> resolver) : ITenantIdentifierSource
{
    /// <inheritdoc/>
    public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
    {
        Argument.IsNotNull(context);

        return TenantIdentifierSourceResult.Found(resolver(context));
    }
}
