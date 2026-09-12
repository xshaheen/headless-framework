// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class IdentityTenantModelTests : TestBase
{
    [Fact]
    public void should_preserve_original_v2_identity_schema_without_opt_in() =>
        _AssertOriginalSchema<UnscopedV2>(false);

    [Fact]
    public void should_preserve_original_v3_identity_schema_without_opt_in() => _AssertOriginalSchema<UnscopedV3>(true);

    private static void _AssertOriginalSchema<TPolicy>(bool passkeys)
        where TPolicy : IPolicy
    {
        using var provider = _CreateProvider<TPolicy>(passkeys);
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IdentityContext<TPolicy>>();
        var model = db.Model;
        model.FindEntityType(typeof(User))!.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("Id");
        model.FindEntityType(typeof(Role))!.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("Id");
        model
            .FindEntityType(typeof(Membership))!
            .FindPrimaryKey()!
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("UserId", "RoleId");
        model
            .FindEntityType(typeof(Login))!
            .FindPrimaryKey()!
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("LoginProvider", "ProviderKey");
        model
            .FindEntityType(typeof(Token))!
            .FindPrimaryKey()!
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("UserId", "LoginProvider", "Name");
        model.FindEntityType(typeof(UserClaim))!.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("Id");
        model.FindEntityType(typeof(RoleClaim))!.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("Id");
        model.GetEntityTypes().Where(x => !x.IsOwned()).Should().OnlyContain(x => !x.IsTenantOwned());
        if (passkeys)
        {
            model
                .FindEntityType(typeof(Passkey))!
                .FindPrimaryKey()!
                .Properties.Select(x => x.Name)
                .Should()
                .Equal("CredentialId");
        }
        else
        {
            model.FindEntityType(typeof(Passkey)).Should().BeNull();
        }
    }

    [Fact]
    public void should_scope_custom_generic_v2_entities_without_reintroducing_passkeys() =>
        _AssertScoped<ScopedV2>(false);

    [Fact]
    public void should_scope_custom_generic_v3_entities_and_passkeys() => _AssertScoped<ScopedV3>(true);

    private static void _AssertScoped<TPolicy>(bool passkeys)
        where TPolicy : IPolicy
    {
        using var provider = _CreateProvider<TPolicy>(passkeys);
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IdentityContext<TPolicy>>();
        var model = db.GetService<IDesignTimeModel>().Model;
        var roots = model.GetEntityTypes().Where(x => !x.IsOwned()).ToArray();
        roots.Should().HaveCount(passkeys ? 8 : 7);
        roots
            .Should()
            .OnlyContain(x =>
                x.IsTenantOwned()
                && !x.FindProperty("TenantId")!.IsNullable
                && x.FindProperty("TenantId")!.IsConcurrencyToken
            );
        roots.Should().OnlyContain(x => x.FindProperty("TenantId")!.GetMaxLength() == 41);
        foreach (var principal in new[] { typeof(User), typeof(Role) })
        {
            var entity = model.FindEntityType(principal)!;
            entity.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("Id");
            entity
                .GetKeys()
                .Single(x => !x.IsPrimaryKey())
                .Properties.Select(x => x.Name)
                .Should()
                .Equal("TenantId", "Id");
            entity.FindProperty("Id")!.GetMaxLength().Should().Be(128);
        }

        var user = model.FindEntityType(typeof(User))!;
        user.GetIndexes()
            .Single(x => x.GetDatabaseName() == "UserNameIndex")
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("NormalizedUserName", "TenantId");
        user.GetIndexes().Single(x => x.GetDatabaseName() == "EmailIndex").IsUnique.Should().BeFalse();
        model
            .FindEntityType(typeof(Role))!
            .GetIndexes()
            .Single(x => x.GetDatabaseName() == "RoleNameIndex")
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("NormalizedName", "TenantId");
        model
            .FindEntityType(typeof(Membership))!
            .FindPrimaryKey()!
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("UserId", "RoleId");
        model
            .FindEntityType(typeof(Login))!
            .FindPrimaryKey()!
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("LoginProvider", "ProviderKey");
        model
            .FindEntityType(typeof(Token))!
            .FindPrimaryKey()!
            .Properties.Select(x => x.Name)
            .Should()
            .Equal("UserId", "LoginProvider", "Name");
        model.FindEntityType(typeof(UserClaim))!.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("Id");
        model.FindEntityType(typeof(RoleClaim))!.FindPrimaryKey()!.Properties.Select(x => x.Name).Should().Equal("Id");
        var foreignKeys = roots.SelectMany(x => x.GetForeignKeys()).ToArray();
        foreignKeys.Should().HaveCount(passkeys ? 7 : 6);
        foreignKeys
            .Should()
            .OnlyContain(x =>
                x.Properties.Count == 2
                && x.Properties[0].Name == "TenantId"
                && x.PrincipalKey.Properties[0].Name == "TenantId"
                && x.IsRequired
            );
        if (passkeys)
        {
            model
                .FindEntityType(typeof(Passkey))!
                .FindPrimaryKey()!
                .Properties.Select(x => x.Name)
                .Should()
                .Equal("CredentialId");
            model.FindEntityType(typeof(Passkey))!.FindProperty("CredentialId")!.GetMaxLength().Should().Be(1024);
            model
                .GetEntityTypes()
                .Where(x => x.IsOwned())
                .Should()
                .OnlyContain(x => x.FindProperty("TenantId") == null);
        }
        else
        {
            model.FindEntityType(typeof(Passkey)).Should().BeNull();
        }
    }

    [Fact]
    public void should_preserve_custom_navigations_delete_behavior_and_relationship_annotations()
    {
        using var provider = _CreateProvider<NavigationPolicy>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IdentityContext<NavigationPolicy>>();
        var foreignKey = db.GetService<IDesignTimeModel>()
            .Model.FindEntityType(typeof(UserClaim))!
            .GetForeignKeys()
            .Single();
        foreignKey.DependentToPrincipal!.Name.Should().Be("User");
        foreignKey.PrincipalToDependent!.Name.Should().Be("Claims");
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        foreignKey["Application:Link"].Should().Be("claims");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_stamp_shadow_tenant_before_tracking_and_keep_persisted_identity_keys_immutable(bool bypass)
    {
        using var provider = _CreateProvider<ScopedV3>();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentTenant>().Id = "tenant-a";
        using var db = scope.ServiceProvider.GetRequiredService<IdentityContext<ScopedV3>>();
        var user = new User { Id = "user" };
        var role = new Role { Id = "role" };
        db.Add(user);
        db.Add(role);
        db.Entry(user).Property("TenantId").CurrentValue.Should().Be("tenant-a");
        db.Entry(role).Property("TenantId").CurrentValue.Should().Be("tenant-a");
        db.Entry(user).State = EntityState.Unchanged;
        using var bypassScope = bypass
            ? scope.ServiceProvider.GetRequiredService<ITenantWriteGuardBypass>().BeginBypass()
            : null;
        var reassign = () =>
        {
            db.Entry(user).Property("TenantId").CurrentValue = "tenant-b";
            db.ChangeTracker.DetectChanges();
        };
        reassign.Should().Throw<InvalidOperationException>().WithMessage("*part of a key*");
    }

    [Fact]
    public void should_reject_missing_tenant_before_identity_key_tracking()
    {
        using var provider = _CreateProvider<ScopedV3>();
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IdentityContext<ScopedV3>>();
        var add = () => db.Add(new User { Id = "user" });
        add.Should().Throw<MissingTenantContextException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_generate_migration_constraints_with_original_primary_keys(bool sqlServer)
    {
        using var provider = _CreateProvider<ScopedV3>(sqlServer: sqlServer);
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IdentityContext<ScopedV3>>();
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        tables.Should().HaveCount(8);
        tables.Single(x => x.Name == "AspNetUsers").PrimaryKey!.Columns.Should().Equal("Id");
        tables
            .Single(x => x.Name == "AspNetUsers")
            .UniqueConstraints.Should()
            .ContainSingle(x => x.Columns.SequenceEqual(new[] { "TenantId", "Id" }));
        tables.Single(x => x.Name == "AspNetUserRoles").PrimaryKey!.Columns.Should().Equal("UserId", "RoleId");
        tables
            .SelectMany(x => x.ForeignKeys)
            .Should()
            .HaveCount(7)
            .And.OnlyContain(x => x.Columns.Length == 2 && x.Columns[0] == "TenantId");
        tables
            .SelectMany(x => x.Columns)
            .Where(x => x.Name == "TenantId")
            .Should()
            .OnlyContain(x => !x.IsNullable && x.MaxLength == 41);
    }

    [Fact]
    public void should_propagate_safe_explicit_principal_and_tenant_lengths()
    {
        using var provider = _CreateProvider<SafeLengths>(sqlServer: true);
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IdentityContext<SafeLengths>>();
        db.Model.FindEntityType(typeof(Membership))!.FindProperty("UserId")!.GetMaxLength().Should().Be(96);
        db.Model.FindEntityType(typeof(Token))!.FindProperty("UserId")!.GetMaxLength().Should().Be(96);
        db.Model.GetEntityTypes()
            .Where(x => !x.IsOwned())
            .Should()
            .OnlyContain(x => x.FindProperty("TenantId")!.GetMaxLength() == 64);
    }

    [Fact]
    public void should_reject_unsafe_sql_server_key_lengths() => _AssertInvalid<UnsafeLengths>("*900-byte*");

    [Fact]
    public void should_reject_explicit_unbounded_tenant_store_type() =>
        _AssertInvalid<UnboundedTenantType>("*explicit bounded string column type*", sqlServer: false);

    [Fact]
    public void should_reject_explicit_unbounded_identity_key_store_type() =>
        _AssertInvalid<UnboundedIdType>("*explicit bounded string column type*", sqlServer: false);

    [Fact]
    public void should_preserve_explicit_bounded_store_types_and_propagate_their_lengths()
    {
        using var provider = _CreateProvider<BoundedStoreTypes>(sqlServer: true);
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IdentityContext<BoundedStoreTypes>>();
        var model = db.GetService<IDesignTimeModel>().Model;
        model.FindEntityType(typeof(User))!.FindProperty("Id")!.GetColumnType().Should().Be("nvarchar(96)");
        model.FindEntityType(typeof(Membership))!.FindProperty("UserId")!.GetMaxLength().Should().Be(96);
        model
            .GetEntityTypes()
            .Where(x => !x.IsOwned())
            .Should()
            .OnlyContain(x => x.FindProperty("TenantId")!.GetMaxLength() == 64);
    }

    [Fact]
    public void should_reject_inconsistent_tenant_lengths() =>
        _AssertInvalid<ConflictingTenantLengths>("*consistent tenant length*");

    [Fact]
    public void should_reject_explicitly_narrowed_child_identifiers() =>
        _AssertInvalid<NarrowChildId>("*cannot be shorter*");

    [Fact]
    public void should_reject_ambiguous_identity_relationships() =>
        _AssertInvalid<AmbiguousRelationship>("*ambiguous*");

    private static void _AssertInvalid<TPolicy>(string message, bool sqlServer = true)
        where TPolicy : IPolicy
    {
        using var provider = _CreateProvider<TPolicy>(sqlServer: sqlServer);
        using var scope = provider.CreateScope();
        var build = () => scope.ServiceProvider.GetRequiredService<IdentityContext<TPolicy>>();
        build.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    private static ServiceProvider _CreateProvider<TPolicy>(bool passkeys = true, bool sqlServer = false)
        where TPolicy : IPolicy
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<TestCurrentTenant>();
        services.AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<TestCurrentTenant>());
        services.AddHeadlessDbContextServices();
        services.AddHeadlessTenantWriteGuard();
        services.Configure<IdentityOptions>(options =>
            options.Stores.SchemaVersion = passkeys ? IdentitySchemaVersions.Version3 : IdentitySchemaVersions.Version2
        );
        services.AddDbContext<IdentityContext<TPolicy>>(options =>
        {
            if (sqlServer)
            {
                options.UseSqlServer(
                    "Server=localhost;Database=identity-model-only;User Id=sa;Password=model-only-password;TrustServerCertificate=True"
                );
            }
            else
            {
                options.UseNpgsql("Host=localhost;Database=identity-model-only");
            }
            options.AddHeadlessExtension();
        });
        return services.BuildServiceProvider();
    }

    private sealed class IdentityContext<TPolicy>(
        HeadlessDbContextServices services,
        DbContextOptions<IdentityContext<TPolicy>> options
    )
        : HeadlessIdentityDbContext<User, Role, string, UserClaim, Membership, Login, RoleClaim, Token, Passkey>(
            services,
            options
        )
        where TPolicy : IPolicy
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<User>().HasMany(x => x.Claims).WithOne(x => x.User).HasForeignKey(x => x.UserId);
            if (TPolicy.Scoped)
            {
                ConfigureTenantOwnedIdentity(builder);
                ConfigureTenantOwnedIdentity(builder);
            }
            TPolicy.Configure(builder);
        }
    }

    private interface IPolicy
    {
        static virtual bool Scoped => true;
        static virtual void Configure(ModelBuilder builder) { }
    }

    private sealed class UnscopedV2 : IPolicy
    {
        public static bool Scoped => false;
    }

    private sealed class UnscopedV3 : IPolicy
    {
        public static bool Scoped => false;
    }

    private sealed class ScopedV2 : IPolicy;

    private sealed class ScopedV3 : IPolicy;

    private sealed class NavigationPolicy : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder
                .Entity<UserClaim>()
                .HasOne(x => x.User)
                .WithMany(x => x.Claims)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasAnnotation("Application:Link", "claims");
    }

    private sealed class SafeLengths : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<User>().Property(x => x.Id).HasMaxLength(96);
            builder.Entity<User>().Property<string>("TenantId").HasMaxLength(64);
        }
    }

    private sealed class BoundedStoreTypes : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<User>().Property(x => x.Id).HasColumnType("nvarchar(96)");
            builder.Entity<User>().Property<string>("TenantId").HasColumnType("nvarchar(64)");
        }
    }

    private sealed class UnboundedTenantType : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder.Entity<User>().Property<string>("TenantId").HasColumnType("text");
    }

    private sealed class UnboundedIdType : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder.Entity<User>().Property(x => x.Id).HasColumnType("text");
    }

    private sealed class UnsafeLengths : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<User>().Property(x => x.Id).HasMaxLength(256);
            builder.Entity<Role>().Property(x => x.Id).HasMaxLength(256);
        }
    }

    private sealed class ConflictingTenantLengths : IPolicy
    {
        public static void Configure(ModelBuilder builder)
        {
            builder.Entity<User>().Property<string>("TenantId").HasMaxLength(64);
            builder.Entity<Role>().Property<string>("TenantId").HasMaxLength(32);
        }
    }

    private sealed class NarrowChildId : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder.Entity<Membership>().Property(x => x.UserId).HasMaxLength(32);
    }

    private sealed class AmbiguousRelationship : IPolicy
    {
        public static void Configure(ModelBuilder builder) =>
            builder.Entity<UserClaim>().HasOne<User>().WithMany().HasForeignKey("OtherUserId");
    }

    private sealed class User : IdentityUser<string>
    {
        public List<UserClaim> Claims { get; set; } = [];
    }

    private sealed class Role : IdentityRole<string>;

    private sealed class UserClaim : IdentityUserClaim<string>
    {
        public User User { get; set; } = null!;
    }

    private sealed class Membership : IdentityUserRole<string>;

    private sealed class Login : IdentityUserLogin<string>;

    private sealed class RoleClaim : IdentityRoleClaim<string>;

    private sealed class Token : IdentityUserToken<string>;

    private sealed class Passkey : IdentityUserPasskey<string>;
}
