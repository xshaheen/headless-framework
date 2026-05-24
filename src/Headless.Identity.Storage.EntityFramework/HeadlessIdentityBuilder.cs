// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

[PublicAPI]
public sealed class HeadlessIdentityBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
