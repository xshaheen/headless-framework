// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

[PublicAPI]
public sealed class HeadlessIdentitySetupBuilder(IServiceCollection services)
{
    internal IServiceCollection Services { get; } = services;

    internal List<object> Extensions { get; } = [];

    internal void RegisterExtension(object extension) => Extensions.Add(extension);
}
