// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.GrantProviders;
using Headless.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Grants;

/// <summary>Resolves and exposes the ordered list of active grant providers.</summary>
public interface IPermissionGrantProviderManager
{
    /// <summary>
    /// Grant providers ordered by registration priority; last-registered has the highest priority index.
    /// The built-in order (lowest to highest) is Role then User.
    /// </summary>
    IReadOnlyList<IPermissionGrantProvider> ValueProviders { get; }
}
