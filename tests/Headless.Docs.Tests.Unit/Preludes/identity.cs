// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application user, role, context, and variables the identity guide's examples assume.

global using static IdentityAmbient;
// ASP.NET Core Identity and EF Core namespaces the examples use; a consumer's IDE adds these usings.
global using Microsoft.AspNetCore.Identity;
global using Microsoft.EntityFrameworkCore;
using Headless.EntityFramework;

public sealed class AppUser : IdentityUser<Guid>;

public sealed class AppRole : IdentityRole<Guid>;

public sealed class AppDbContext(HeadlessDbContextServices services, DbContextOptions<AppDbContext> options)
    : HeadlessIdentityDbContext<
        AppUser,
        AppRole,
        Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>,
        IdentityUserPasskey<Guid>
    >(services, options)
{
    public override string? DefaultSchema => null;
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class IdentityAmbient
{
    public static string connectionString => "";

    public static ModelBuilder modelBuilder => null!;
}
