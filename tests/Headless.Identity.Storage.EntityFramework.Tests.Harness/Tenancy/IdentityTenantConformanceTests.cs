// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Security.Claims;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests.Tenancy;

public abstract class IdentityTenantConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : TenantIdentityFixture<DefaultIdentityPolicy>
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ResetAsync(AbortToken);
    }

    [Fact]
    public async Task should_create_lookup_assign_claim_login_and_token_through_real_managers_per_tenant()
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tenant in new[] { "acme", "ACME" })
        {
            fixture.CurrentTenant.Id = tenant;
            await using var scope = fixture.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<TenantIdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<TenantIdentityRole>>();
            var user = new TenantIdentityUser { UserName = "shared-user", Email = "shared@example.test" };
            _Success(await users.CreateAsync(user));
            ids[tenant] = user.Id;
            _Success(await roles.CreateAsync(new TenantIdentityRole { Name = "shared-role" }));
            _Success(await users.AddToRoleAsync(user, "shared-role"));
            _Success(await users.AddClaimAsync(user, new Claim("tenant-claim", tenant)));
            var role = (await roles.FindByNameAsync("shared-role"))!;
            _Success(await roles.AddClaimAsync(role, new Claim("role-claim", tenant)));
            _Success(
                await users.AddLoginAsync(
                    user,
                    new UserLoginInfo(
                        "provider",
                        string.Equals(tenant, "acme", StringComparison.Ordinal) ? "login-lower" : "login-upper",
                        "Provider"
                    )
                )
            );
            _Success(await users.SetAuthenticationTokenAsync(user, "provider", "token", tenant));
            var duplicateName = await users.CreateAsync(
                new TenantIdentityUser { UserName = "shared-user", Email = "other@example.test" }
            );
            duplicateName.Errors.Should().Contain(x => x.Code == "DuplicateUserName");
            (await roles.CreateAsync(new TenantIdentityRole { Name = "shared-role" }))
                .Errors.Should()
                .Contain(x => x.Code == "DuplicateRoleName");
            (await users.CreateAsync(new TenantIdentityUser { UserName = "other", Email = "shared@example.test" }))
                .Errors.Should()
                .Contain(x => x.Code == "DuplicateEmail");
        }
        foreach (var tenant in new[] { "acme", "ACME" })
        {
            fixture.CurrentTenant.Id = tenant;
            await using var scope = fixture.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<TenantIdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<TenantIdentityRole>>();
            var user = (await users.FindByNameAsync("shared-user"))!;
            user.Id.Should().Be(ids[tenant]);
            (await users.FindByEmailAsync("shared@example.test"))!.Id.Should().Be(user.Id);
            (await users.IsInRoleAsync(user, "shared-role")).Should().BeTrue();
            (await users.GetClaimsAsync(user))
                .Should()
                .ContainSingle(x => x.Type == "tenant-claim" && x.Value == tenant);
            (await roles.GetClaimsAsync((await roles.FindByNameAsync("shared-role"))!))
                .Should()
                .ContainSingle(x => x.Value == tenant);
            (
                await users.FindByLoginAsync(
                    "provider",
                    string.Equals(tenant, "acme", StringComparison.Ordinal) ? "login-lower" : "login-upper"
                )
            )!
                .Id.Should()
                .Be(user.Id);
            (
                await users.FindByLoginAsync(
                    "provider",
                    string.Equals(tenant, "acme", StringComparison.Ordinal) ? "login-upper" : "login-lower"
                )
            )
                .Should()
                .BeNull();
            (await users.GetAuthenticationTokenAsync(user, "provider", "token")).Should().Be(tenant);
            (await users.FindByIdAsync(ids[string.Equals(tenant, "acme", StringComparison.Ordinal) ? "ACME" : "acme"]))
                .Should()
                .BeNull();
        }
    }

    [Fact]
    public async Task should_support_passkey_lifecycle_in_fresh_store_scopes_and_isolate_lookup()
    {
        var userId = await _CreateUserAsync("acme");
        byte[] credential = [1, 3, 5, 7];
        await _WithUserAsync(
            "acme",
            userId,
            async (users, user) => _Success(await users.AddOrUpdatePasskeyAsync(user, _Passkey(credential)))
        );
        fixture.CurrentTenant.Id = "ACME";
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            (
                await scope
                    .ServiceProvider.GetRequiredService<UserManager<TenantIdentityUser>>()
                    .FindByPasskeyIdAsync(credential)
            )
                .Should()
                .BeNull();
        }
        await _WithUserAsync(
            "acme",
            userId,
            async (users, user) =>
            {
                (await users.GetPasskeysAsync(user)).Should().ContainSingle();
                (await users.FindByPasskeyIdAsync(credential))!.Id.Should().Be(userId);
                var key = (await users.GetPasskeyAsync(user, credential))!;
                key.Name = "updated";
                key.SignCount = 7;
                _Success(await users.AddOrUpdatePasskeyAsync(user, key));
            }
        );
        await _WithUserAsync(
            "acme",
            userId,
            async (users, user) =>
            {
                var key = (await users.GetPasskeyAsync(user, credential))!;
                key.Name.Should().Be("updated");
                key.SignCount.Should().Be(7);
                _Success(await users.RemovePasskeyAsync(user, credential));
            }
        );
        await _WithUserAsync(
            "acme",
            userId,
            async (users, user) =>
            {
                (await users.GetPasskeysAsync(user)).Should().BeEmpty();
                (await users.FindByPasskeyIdAsync(credential)).Should().BeNull();
            }
        );
    }

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    [InlineData("login")]
    [InlineData("passkey")]
    public async Task should_preserve_global_identity_keys_across_tenants(string kind)
    {
        var first = await _CreateUserAsync("acme");
        var second = await _CreateUserAsync("ACME");
        byte[] credential = [10, 20, 30];
        fixture.CurrentTenant.Id = "acme";
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantIdentityContext<DefaultIdentityPolicy>>();
            if (string.Equals(kind, "role", StringComparison.Ordinal))
            {
                db.Add(
                    new TenantIdentityRole
                    {
                        Id = "global-role",
                        Name = "first",
                        NormalizedName = "FIRST",
                    }
                );
                await db.SaveChangesAsync(AbortToken);
            }
            if (string.Equals(kind, "login", StringComparison.Ordinal))
            {
                db.Add(
                    new IdentityUserLogin<string>
                    {
                        UserId = first,
                        LoginProvider = "provider",
                        ProviderKey = "global",
                    }
                );
                await db.SaveChangesAsync(AbortToken);
            }
            if (string.Equals(kind, "passkey", StringComparison.Ordinal))
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<TenantIdentityUser>>();
                _Success(
                    await users.AddOrUpdatePasskeyAsync((await users.FindByIdAsync(first))!, _Passkey(credential))
                );
            }
        }
        fixture.CurrentTenant.Id = "ACME";
        await using var attack = fixture.Services.CreateAsyncScope();
        var target = attack.ServiceProvider.GetRequiredService<TenantIdentityContext<DefaultIdentityPolicy>>();
        if (string.Equals(kind, "passkey", StringComparison.Ordinal))
        {
            var users = attack.ServiceProvider.GetRequiredService<UserManager<TenantIdentityUser>>();
            var user = (await users.FindByIdAsync(second))!;
            var failure = (
                await users
                    .Invoking(x => x.AddOrUpdatePasskeyAsync(user, _Passkey(credential)))
                    .Should()
                    .ThrowAsync<DbUpdateException>()
            ).Which;
            _UniqueViolation(failure);
        }
        else
        {
            object duplicate = kind switch
            {
                "user" => new TenantIdentityUser
                {
                    Id = first,
                    UserName = "duplicate",
                    NormalizedUserName = "DUPLICATE",
                },
                "role" => new TenantIdentityRole
                {
                    Id = "global-role",
                    Name = "duplicate",
                    NormalizedName = "DUPLICATE",
                },
                _ => new IdentityUserLogin<string>
                {
                    UserId = second,
                    LoginProvider = "provider",
                    ProviderKey = "global",
                },
            };
#pragma warning disable VSTHRD103 // Exercise synchronous Add and tenant stamping before the database rejects the duplicate key.
            target.Add(duplicate);
#pragma warning restore VSTHRD103
            var failure = (
                await target.Invoking(x => x.SaveChangesAsync(AbortToken)).Should().ThrowAsync<DbUpdateException>()
            ).Which;
            _UniqueViolation(failure);
        }
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("membership-user")]
    [InlineData("membership-role")]
    [InlineData("login")]
    [InlineData("role-claim")]
    [InlineData("token")]
    [InlineData("passkey")]
    public async Task should_reject_direct_sql_cross_tenant_links_even_under_guard_bypass(string kind)
    {
        var victimUser = await _CreateUserAsync("ACME");
        var ownUser = await _CreateUserAsync("acme");
        var victimRole = await _CreateRoleAsync("ACME");
        var ownRole = await _CreateRoleAsync("acme");
        fixture.CurrentTenant.Id = "acme";
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantIdentityContext<DefaultIdentityPolicy>>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ITenantWriteGuardBypass>().BeginBypass();
        var (table, columns, values) = kind switch
        {
            "claim" => (
                "AspNetUserClaims",
                new[] { "UserId", "TenantId", "ClaimType", "ClaimValue" },
                new object[] { victimUser, "acme", "raw", "bad" }
            ),
            "membership-user" => ("AspNetUserRoles", ["UserId", "RoleId", "TenantId"], [victimUser, ownRole, "acme"]),
            "membership-role" => ("AspNetUserRoles", ["UserId", "RoleId", "TenantId"], [ownUser, victimRole, "acme"]),
            "login" => (
                "AspNetUserLogins",
                ["UserId", "LoginProvider", "ProviderKey", "TenantId"],
                [victimUser, "raw", "raw", "acme"]
            ),
            "role-claim" => (
                "AspNetRoleClaims",
                ["RoleId", "TenantId", "ClaimType", "ClaimValue"],
                [victimRole, "acme", "raw", "bad"]
            ),
            "token" => (
                "AspNetUserTokens",
                ["UserId", "LoginProvider", "Name", "Value", "TenantId"],
                [victimUser, "raw", "raw", "bad", "acme"]
            ),
            _ => (
                "AspNetUserPasskeys",
                ["UserId", "CredentialId", "Data", "TenantId"],
                [victimUser, "7B"u8.ToArray(), "{}", "acme"]
            ),
        };
        var insert = async () => await _InsertAsync(db, table, columns, values);
        var failure = (await insert.Should().ThrowAsync<DbException>()).Which;
        if (failure is PostgresException postgres)
        {
            postgres.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        }
        else
        {
            failure.Should().BeOfType<SqlException>().Which.Number.Should().Be(547);
        }
        var countSql =
            $"SELECT COUNT(*) AS {db.GetService<ISqlGenerationHelper>().DelimitIdentifier("Value")} FROM {db.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, "tenant_identity")}";
        (await db.Database.SqlQueryRaw<int>(countSql).SingleAsync(AbortToken)).Should().Be(0);
        (await db.Users.IgnoreQueryFilters().CountAsync(AbortToken)).Should().Be(2);
        (await db.Roles.IgnoreQueryFilters().CountAsync(AbortToken)).Should().Be(2);
    }

    [Fact]
    public async Task should_reject_identity_add_under_a_and_save_under_b_before_persistence()
    {
        fixture.CurrentTenant.Id = "acme";
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantIdentityContext<DefaultIdentityPolicy>>();
        var role = new TenantIdentityRole { Name = "new-role" };
        db.Add(role);
        db.Entry(role).Property("TenantId").CurrentValue.Should().Be("acme");
        fixture.CurrentTenant.Id = "ACME";
        await db.Invoking(x => x.SaveChangesAsync(AbortToken)).Should().ThrowAsync<CrossTenantWriteException>();
        db.Entry(role).Property("TenantId").CurrentValue.Should().Be("acme");
        (await db.Roles.IgnoreQueryFilters().CountAsync(AbortToken)).Should().Be(0);
    }

    [Fact]
    public void should_preserve_pk_shapes_and_generate_required_ak_fk_collation_and_indexes()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantIdentityContext<DefaultIdentityPolicy>>();
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var tables = operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name, StringComparer.Ordinal);
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["AspNetUsers"] = ["Id"],
            ["AspNetRoles"] = ["Id"],
            ["AspNetUserClaims"] = ["Id"],
            ["AspNetRoleClaims"] = ["Id"],
            ["AspNetUserRoles"] = ["UserId", "RoleId"],
            ["AspNetUserLogins"] = ["LoginProvider", "ProviderKey"],
            ["AspNetUserTokens"] = ["UserId", "LoginProvider", "Name"],
            ["AspNetUserPasskeys"] = ["CredentialId"],
        };
        tables.Should().HaveCount(8);
        foreach (var (name, key) in expected)
        {
            tables[name].PrimaryKey!.Columns.Should().Equal(key);
            var tenant = tables[name].Columns.Single(x => string.Equals(x.Name, "TenantId", StringComparison.Ordinal));
            tenant.IsNullable.Should().BeFalse();
            tenant
                .Collation.Should()
                .Be(fixture.Provider == TenantDatabaseProvider.SqlServer ? "Latin1_General_100_BIN2" : "C");
            tables[name].CheckConstraints.Should().NotBeEmpty();
        }
        tables["AspNetUsers"].UniqueConstraints.Single().Columns.Should().Equal("TenantId", "Id");
        tables["AspNetRoles"].UniqueConstraints.Single().Columns.Should().Equal("TenantId", "Id");
        var foreignKeys = tables.Values.SelectMany(x => x.ForeignKeys).ToArray();
        foreignKeys
            .Should()
            .HaveCount(7)
            .And.OnlyContain(x =>
                x.Columns.Length == 2
                && x.Columns[0] == "TenantId"
                && x.PrincipalColumns!.SequenceEqual(new[] { "TenantId", "Id" })
            );
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        indexes
            .Single(x => string.Equals(x.Name, "UserNameIndex", StringComparison.Ordinal))
            .Columns.Should()
            .Equal("NormalizedUserName", "TenantId");
        indexes
            .Single(x => string.Equals(x.Name, "RoleNameIndex", StringComparison.Ordinal))
            .Columns.Should()
            .Equal("NormalizedName", "TenantId");
        indexes.Single(x => string.Equals(x.Name, "EmailIndex", StringComparison.Ordinal)).IsUnique.Should().BeFalse();
        fixture
            .MigrationSql.Should()
            .Contain("PRIMARY KEY")
            .And.Contain("FOREIGN KEY")
            .And.Contain("UNIQUE")
            .And.Contain("COLLATE")
            .And.Contain("CHECK");
    }

    [Fact]
    public async Task should_create_schema_v2_and_use_actual_stores_without_passkeys() =>
        await _PolicyBaselineAsync<V2IdentityPolicy>();

    [Fact]
    public async Task should_create_and_use_safe_configured_identity_key_lengths() =>
        await _PolicyBaselineAsync<BoundedIdentityPolicy>();

    private async Task _PolicyBaselineAsync<TPolicy>()
        where TPolicy : ITenantIdentityPolicy
    {
        await using var provider = fixture.CreateServices(fixture.ConfigureIdentityServices<TPolicy>);
        fixture.CurrentTenant.Id = new string('t', TPolicy.CustomLengths ? 64 : 41);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantIdentityContext<TPolicy>>();
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
        {
            await db.Database.ExecuteSqlRawAsync(command.CommandText, AbortToken);
        }
        (db.Model.FindEntityType(typeof(IdentityUserPasskey<string>)) is not null).Should().Be(TPolicy.Passkeys);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TenantIdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<TenantIdentityRole>>();
        var user = new TenantIdentityUser
        {
            Id = new string('u', TPolicy.CustomLengths ? 96 : 128),
            UserName = "boundary",
            Email = "boundary@example.test",
        };
        _Success(await users.CreateAsync(user));
        _Success(
            await roles.CreateAsync(
                new TenantIdentityRole { Id = new string('r', TPolicy.CustomLengths ? 96 : 128), Name = "boundary" }
            )
        );
        _Success(await users.AddToRoleAsync(user, "boundary"));
        _Success(await users.SetAuthenticationTokenAsync(user, "provider", "token", "value"));
        if (TPolicy.Passkeys)
        {
            _Success(await users.AddOrUpdatePasskeyAsync(user, _Passkey([42, 43])));
        }
        db.ChangeTracker.Clear();
        (await users.FindByNameAsync("boundary"))!.Id.Should().Be(user.Id);
        (await users.GetRolesAsync(user)).Should().ContainSingle().Which.Should().Be("boundary");
    }

    [Fact]
    public async Task should_expose_actual_sql_server_passkey_key_limit_without_claiming_mapping_max_works()
    {
        var id = await _CreateUserAsync("tenant-a");
        await _WithUserAsync(
            "tenant-a",
            id,
            async (users, user) =>
            {
                var credential = Enumerable.Repeat((byte)7, 1024).ToArray();
                if (fixture.Provider == TenantDatabaseProvider.SqlServer)
                {
                    var failure = (
                        await users
                            .Invoking(x => x.AddOrUpdatePasskeyAsync(user, _Passkey(credential)))
                            .Should()
                            .ThrowAsync<DbUpdateException>()
                    ).Which;
                    var sqlError = failure.InnerException.Should().BeOfType<SqlException>().Subject;
                    sqlError.Message.Should().Contain("900").And.Contain("maximum");
                }
                else
                {
                    _Success(await users.AddOrUpdatePasskeyAsync(user, _Passkey(credential)));
                }
            }
        );
    }

    private async Task<string> _CreateUserAsync(string tenant)
    {
        fixture.CurrentTenant.Id = tenant;
        await using var scope = fixture.Services.CreateAsyncScope();
        var user = new TenantIdentityUser
        {
            UserName = Guid.NewGuid().ToString(),
            Email = $"{Guid.NewGuid():N}@example.test",
        };
        _Success(await scope.ServiceProvider.GetRequiredService<UserManager<TenantIdentityUser>>().CreateAsync(user));
        return user.Id;
    }

    private async Task<string> _CreateRoleAsync(string tenant)
    {
        fixture.CurrentTenant.Id = tenant;
        await using var scope = fixture.Services.CreateAsyncScope();
        var role = new TenantIdentityRole { Name = Guid.NewGuid().ToString() };
        _Success(await scope.ServiceProvider.GetRequiredService<RoleManager<TenantIdentityRole>>().CreateAsync(role));
        return role.Id;
    }

    private async Task _WithUserAsync(
        string tenant,
        string id,
        Func<UserManager<TenantIdentityUser>, TenantIdentityUser, Task> action
    )
    {
        fixture.CurrentTenant.Id = tenant;
        await using var scope = fixture.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TenantIdentityUser>>();
        await action(users, (await users.FindByIdAsync(id))!);
    }

    private static UserPasskeyInfo _Passkey(byte[] credential) =>
        new(credential, [1, 2, 3], DateTimeOffset.UtcNow, 0, ["internal"], true, false, false, [4], [5]);

    private static void _Success(IdentityResult result) =>
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(x => $"{x.Code}: {x.Description}")));

    private static void _UniqueViolation(DbUpdateException failure)
    {
        if (failure.InnerException is PostgresException postgres)
        {
            postgres.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }
        else
        {
            failure.InnerException.Should().BeOfType<SqlException>().Which.Number.Should().BeOneOf(2601, 2627);
        }
    }

    private static Task<int> _InsertAsync(DbContext db, string table, string[] columns, object[] values)
    {
        var helper = db.GetService<ISqlGenerationHelper>();
        var names = string.Join(", ", columns.Select(helper.DelimitIdentifier));
        var parameters = string.Join(
            ", ",
            values.Select(
                (_, index) =>
                    string.Equals(columns[index], "Data", StringComparison.Ordinal) && db.Database.IsNpgsql()
                        ? $"CAST({{{index}}} AS jsonb)"
                        : $"{{{index}}}"
            )
        );
        var sql = $"INSERT INTO {helper.DelimitIdentifier(table, "tenant_identity")} ({names}) VALUES ({parameters})";
        return db.Database.ExecuteSqlRawAsync(sql, values, AbortToken);
    }
}
