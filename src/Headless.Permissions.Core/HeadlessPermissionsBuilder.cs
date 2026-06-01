// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions;

[PublicAPI]
public sealed class HeadlessPermissionsBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
