// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks;

[PublicAPI]
public sealed class HeadlessDistributedLocksBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
