// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Tenancy;

public sealed class TenantIdentityUser : IdentityUser;

public sealed class TenantIdentityRole : IdentityRole;

public interface ITenantIdentityPolicy
{
    static virtual bool Passkeys => true;
    static virtual bool CustomLengths => false;
}

public sealed class DefaultIdentityPolicy : ITenantIdentityPolicy;

public sealed class V2IdentityPolicy : ITenantIdentityPolicy
{
    public static bool Passkeys => false;
}

public sealed class BoundedIdentityPolicy : ITenantIdentityPolicy
{
    public static bool CustomLengths => true;
}

public sealed class TenantIdentityContext<TPolicy>(
    HeadlessDbContextServices services,
    DbContextOptions<TenantIdentityContext<TPolicy>> options
)
    : HeadlessIdentityDbContext<
        TenantIdentityUser,
        TenantIdentityRole,
        string,
        IdentityUserClaim<string>,
        IdentityUserRole<string>,
        IdentityUserLogin<string>,
        IdentityRoleClaim<string>,
        IdentityUserToken<string>,
        IdentityUserPasskey<string>
    >(services, options)
    where TPolicy : ITenantIdentityPolicy
{
    public override string DefaultSchema =>
        !TPolicy.Passkeys ? "tenant_identity_v2"
        : TPolicy.CustomLengths ? "tenant_identity_bounded"
        : "tenant_identity";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureTenantOwnedIdentity(modelBuilder);
        if (TPolicy.CustomLengths)
        {
            modelBuilder.Entity<TenantIdentityUser>().Property(x => x.Id).HasMaxLength(96);
            modelBuilder.Entity<TenantIdentityRole>().Property(x => x.Id).HasMaxLength(96);
            modelBuilder.Entity<TenantIdentityUser>().Property<string>("TenantId").HasMaxLength(64);
        }
    }
}

public abstract class TenantIdentityFixture<TPolicy>(TenantDatabaseProvider provider) : TenantDatabaseFixture(provider)
    where TPolicy : ITenantIdentityPolicy
{
    protected override void ConfigureServices(IServiceCollection services) =>
        ConfigureIdentityServices<TPolicy>(services);

    public void ConfigureIdentityServices<TConfiguration>(IServiceCollection services)
        where TConfiguration : ITenantIdentityPolicy
    {
        services
            .AddIdentityCore<TenantIdentityUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<TenantIdentityRole>()
            .AddEntityFrameworkStores<TenantIdentityContext<TConfiguration>>();
        services.AddHeadlessDbContext<
            TenantIdentityContext<TConfiguration>,
            TenantIdentityUser,
            TenantIdentityRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>,
            IdentityUserPasskey<string>
        >(ConfigureOptions);
        services.Configure<IdentityOptions>(options =>
            options.Stores.SchemaVersion = TConfiguration.Passkeys
                ? IdentitySchemaVersions.Version3
                : IdentitySchemaVersions.Version2
        );
    }

    protected override DbContext GetContext(IServiceProvider services) =>
        services.GetRequiredService<TenantIdentityContext<TPolicy>>();
}
