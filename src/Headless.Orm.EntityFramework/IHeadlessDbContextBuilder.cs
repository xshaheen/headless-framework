// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Builder returned by <c>AddHeadlessDbContextServices</c> so optional event tiers can be chained:
/// <c>services.AddHeadlessDbContextServices().AddDomainEvents().AddIntegrationEventOutbox()</c>.
/// Satellite packages add discoverable <c>.AddX()</c> extension methods on this builder.
/// </summary>
[PublicAPI]
public interface IHeadlessDbContextBuilder
{
    /// <summary>The underlying service collection, for advanced registration.</summary>
    IServiceCollection Services { get; }
}

internal sealed class HeadlessDbContextBuilder(IServiceCollection services) : IHeadlessDbContextBuilder
{
    public IServiceCollection Services { get; } = services;
}
