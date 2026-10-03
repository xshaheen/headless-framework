// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

internal sealed class HeadlessDbContextBuilder(IServiceCollection services) : IHeadlessDbContextBuilder
{
    public IServiceCollection Services { get; } = services;
}
