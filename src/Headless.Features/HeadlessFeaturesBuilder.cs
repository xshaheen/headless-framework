// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features;

/// <summary>Returned by <c>AddHeadlessFeatures</c> to allow further service registration after the core features infrastructure is wired up.</summary>
[PublicAPI]
public sealed class HeadlessFeaturesBuilder(IServiceCollection services)
{
    /// <summary>Gets the underlying <see cref="IServiceCollection"/> for additional service registration.</summary>
    public IServiceCollection Services { get; } = services;
}
