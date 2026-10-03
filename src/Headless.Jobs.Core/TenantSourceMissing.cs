// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Models;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Jobs;

/// <summary>Shared "no tenant source" predicate used by the Jobs tenancy validators.</summary>
internal static class TenantSourceMissing
{
    /// <summary>
    /// Returns <see langword="true"/> when the host registered a custom <see cref="ICurrentTenant"/> implementation
    /// beyond the framework defaults (<c>CurrentTenant</c> or <c>NullCurrentTenant</c>). Used by tenancy validators
    /// to recognize consumer-supplied tenant sources that bypass the seam manifest.
    /// </summary>
    public static bool HasConsumerOverride(IServiceProvider services)
    {
        var currentTenant = services.GetService<ICurrentTenant>();
        return currentTenant is not null and not CurrentTenant and not NullCurrentTenant;
    }
}
