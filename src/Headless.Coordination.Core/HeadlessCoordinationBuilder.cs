// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Coordination;

[PublicAPI]
public sealed class HeadlessCoordinationBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
