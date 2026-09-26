// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application providers, context, caches, and variables the permissions guide's examples assume.

// ASP.NET Core and EF Core namespaces the examples use; a consumer's IDE adds these usings.
global using System.Security.Claims;
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.EntityFrameworkCore;
global using static PermissionsAmbient;
using Headless.Abstractions;
using Headless.Permissions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Seeders;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public sealed class OrderPermissionProvider : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context) { }
}

public sealed class MyPolicyCache
{
    public string RoleId => "";

    public bool Tracks(string permissionName) => false;

    public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class UserConfig
{
    public required IReadOnlySet<string> GrantedPolicies { get; init; }

    public required IReadOnlyDictionary<string, string?> Features { get; init; }

    public required IReadOnlyDictionary<string, string?> Settings { get; init; }
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class PermissionsAmbient
{
    public static string connectionString => "";

    public static IPermissionManager permissions => null!;

    public static IPermissionManager permissionManager => null!;

    public static ICurrentUser currentUser => null!;

    public static IGrantPermissionsSeedHelper seedHelper => null!;
}
