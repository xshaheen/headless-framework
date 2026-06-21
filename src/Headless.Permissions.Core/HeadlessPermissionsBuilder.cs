// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions;

/// <summary>
/// Returned by <see cref="SetupPermissions.AddHeadlessPermissions"/> to allow post-registration service
/// configuration. Exposes the underlying <see cref="IServiceCollection"/> for further additions.
/// </summary>
[PublicAPI]
public sealed class HeadlessPermissionsBuilder(IServiceCollection services)
{
    /// <summary>The service collection the permissions system was registered into.</summary>
    public IServiceCollection Services { get; } = services;
}
