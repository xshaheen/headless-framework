// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application DbContext, providers, cache, and variables the features guide's examples assume.

global using static FeaturesAmbient;
// Provider SDK types the examples name; a consumer's IDE adds this using.
global using StackExchange.Redis;
using Headless.Features.Definitions;
using Headless.Features.Models;
using Headless.Features.ValueProviders;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public sealed class MyFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public void Define(IFeatureDefinitionContext context) { }
}

public sealed class MyCustomFeatureValueProvider : IFeatureValueReadProvider
{
    public string Name => "Custom";

    public Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IAsyncDisposable>(null!);

    public Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<string?>(null);
}

public sealed class TenantFeatureCache
{
    public string TenantId => "";

    public bool Tracks(string featureName) => false;

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class FeaturesAmbient
{
    public static string connectionString => "";
}
