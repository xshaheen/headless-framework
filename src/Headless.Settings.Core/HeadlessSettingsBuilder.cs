// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Settings;

/// <summary>Returned by <c>AddHeadlessSettings</c> to allow further configuration of the settings feature after the core services have been registered.</summary>
[PublicAPI]
public sealed class HeadlessSettingsBuilder(IServiceCollection services)
{
    /// <summary>Gets the application service collection for further configuration.</summary>
    public IServiceCollection Services { get; } = services;
}
