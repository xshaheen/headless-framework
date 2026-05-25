// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Settings;

[PublicAPI]
public sealed class HeadlessSettingsBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
