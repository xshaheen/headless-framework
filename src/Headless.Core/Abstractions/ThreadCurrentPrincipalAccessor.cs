// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Core;

namespace Headless.Abstractions;

/// <summary>
/// <see cref="CurrentPrincipalAccessor"/> implementation that uses <see cref="Thread.CurrentPrincipal"/>
/// as the fallback when no async-local override is active. Suitable for non-ASP.NET hosted environments
/// (console apps, worker services) that rely on the thread-static principal.
/// </summary>
public class ThreadCurrentPrincipalAccessor : CurrentPrincipalAccessor
{
    /// <inheritdoc/>
    protected override ClaimsPrincipal? GetClaimsPrincipal()
    {
        return Thread.CurrentPrincipal as ClaimsPrincipal;
    }
}
