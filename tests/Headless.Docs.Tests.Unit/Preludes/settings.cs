// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application DbContext, providers, cache, and variables the settings guide's examples assume.

global using static SettingsAmbient;
// Provider SDK types the examples name; a consumer's IDE adds this using.
global using StackExchange.Redis;
using Headless.Settings.Definitions;
using Headless.Settings.Models;
using Headless.Settings.ValueProviders;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public sealed class AppSettingDefinitionProvider : ISettingDefinitionProvider
{
    public void Define(ISettingDefinitionContext context) { }
}

public sealed class MyCustomSettingValueProvider : ISettingValueReadProvider
{
    public string Name => "Custom";

    public Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<string?>(null);

    public Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new List<SettingValue>());
}

public sealed class MyPolicyCache
{
    public bool Tracks(string settingName) => false;

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class SettingsAmbient
{
    public static string connectionString => "";
}
