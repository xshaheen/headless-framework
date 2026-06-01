// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features;

[PublicAPI]
public sealed class HeadlessFeaturesBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
