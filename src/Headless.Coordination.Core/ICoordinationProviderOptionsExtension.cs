// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Coordination;

/// <summary>Setup-time extension hook implemented by each coordination provider package.</summary>
/// <remarks>
/// Provider packages create an internal implementation of this interface and register it via
/// <see cref="HeadlessCoordinationSetupBuilder.RegisterExtension"/>. Exactly one extension must be
/// registered per <c>AddHeadlessCoordination</c> call; the core setup validates this constraint and
/// throws <see cref="InvalidOperationException"/> if zero or multiple providers are found.
/// </remarks>
[PublicAPI]
public interface ICoordinationProviderOptionsExtension
{
    /// <summary>Registers all provider-specific services into <paramref name="services"/>.</summary>
    /// <param name="services">The application's <see cref="IServiceCollection"/>.</param>
    void AddServices(IServiceCollection services);
}
