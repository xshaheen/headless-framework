// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Requirements;

/// <summary>
/// Lists authorization policy names an application can evaluate: policies registered through
/// <see cref="AuthorizationOptions.AddPolicy(string, AuthorizationPolicy)"/> and, when
/// <see cref="PermissionPolicyProvider"/> is the active policy provider, the permission names it resolves as policies.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core keeps registered policies in a private member of <see cref="AuthorizationOptions"/>; the catalog
/// reads it directly because the framework exposes no listing. A framework change that renames or removes that
/// member makes <see cref="GetRegisteredPolicyNamesAsync"/> throw <see cref="MissingMethodException"/>.
/// </para>
/// <para>
/// Policies produced on demand by any other <see cref="IAuthorizationPolicyProvider"/> have no list and are not
/// included.
/// </para>
/// </remarks>
[PublicAPI]
public interface IAuthorizationPolicyCatalog
{
    /// <summary>Gets the names of the policies registered on <see cref="AuthorizationOptions"/>.</summary>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The registered policy names.</returns>
    Task<IReadOnlySet<string>> GetRegisteredPolicyNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the names of every defined permission. These are permission names for a grant check, not necessarily
    /// policy names: a configured <see cref="PermissionManagementOptions.PolicyNamePrefix"/> or a host-owned policy
    /// provider changes which names resolve as policies.
    /// </summary>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The defined permission names.</returns>
    Task<IReadOnlySet<string>> GetPermissionNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every name that resolves to a policy through the active policy provider: the registered policies, plus
    /// the defined permissions (carrying any configured <see cref="PermissionManagementOptions.PolicyNamePrefix"/>)
    /// when <see cref="PermissionPolicyProvider"/> is that provider.
    /// </summary>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>Every listed policy name, each safe to pass to <see cref="IAuthorizationService"/>.</returns>
    Task<IReadOnlySet<string>> GetPolicyNamesAsync(CancellationToken cancellationToken = default);
}
