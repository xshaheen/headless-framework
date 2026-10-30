// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Permissions;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionsUnitOfWorkTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    [Fact]
    public async Task should_commit_a_grant_with_an_ef_unit_and_clear_the_warm_cache_only_after_commit()
    {
        // given a warm cache that says the permission is undefined
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();
        (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Undefined);
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        var dbFactory = host.Services.GetRequiredService<IDbContextFactory<PermissionsTestDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(AbortToken);

        // when
        await using (var unit = await factory.BeginAsync(db, AbortToken))
        {
            await unit.Permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);

            // then the cache keeps the pre-commit value while the unit is open
            (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
                .Should()
                .Be(PermissionGrantStatus.Undefined);

            await unit.CompleteAsync(AbortToken);
        }

        // and the committed grant is read after the commit
        (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Granted);
    }

    [Fact]
    public async Task should_discard_an_ef_store_grant_when_a_raw_ado_unit_rolls_back()
    {
        // given
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        await using var connection = new NpgsqlConnection(Fixture.SqlConnectionString);
        await connection.OpenAsync(AbortToken);

        // when
        await using (var unit = await factory.BeginAsync(connection, AbortToken))
        {
            await unit.Permissions.SetAsync(["Users.Create", "Users.Delete"], "Role", "admin", true, AbortToken);

            (await repository.GetListEnlistedAsync(unit, "Role", "admin", AbortToken)).Should().HaveCount(2);

            await unit.RollbackAsync();
        }

        // then
        (await repository.GetListAsync("Role", "admin", AbortToken))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task should_commit_an_ef_store_delete_with_a_raw_ado_unit()
    {
        // given a committed grant
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        await host
            .Services.GetRequiredService<IPermissionManager>()
            .GrantToRoleAsync("Users.Create", "admin", AbortToken);
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();
        await using var connection = new NpgsqlConnection(Fixture.SqlConnectionString);
        await connection.OpenAsync(AbortToken);

        // when
        await using (var unit = await factory.BeginAsync(connection, AbortToken))
        {
            await unit.Permissions.DeleteAsync("Role", "admin", AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then
        (await repository.GetListAsync("Role", "admin", AbortToken))
            .Should()
            .BeEmpty();
    }

    private IHost _CreateHost()
    {
        return CreateHost(b => b.Services.AddPermissionDefinitionProvider<UsersPermissionDefinitionProvider>());
    }

    [UsedImplicitly]
    internal sealed class UsersPermissionDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            var users = context.AddGroup("Users");
            users.AddChild("Users.Create");
            users.AddChild("Users.Delete");
        }
    }
}

/// <summary>
/// A pooled context that retries on failure: the enlisted call must hand the pooled context its own connection back, and
/// join a unit only from inside the execution strategy that <c>RunAsync(db, …)</c> runs.
/// </summary>
public sealed class PermissionsPooledUnitOfWorkTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    [Fact]
    public async Task should_join_runasync_under_a_retrying_strategy_and_return_the_pooled_context_its_connection()
    {
        // given
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        var dbFactory = host.Services.GetRequiredService<IDbContextFactory<PooledPermissionsDbContext>>();
        var repository = host.Services.GetRequiredService<IPermissionGrantRepository>();

        // when
        await using (var db = await dbFactory.CreateDbContextAsync(AbortToken))
        {
            await factory.RunAsync(
                db,
                (unit, ct) => unit.Permissions.GrantToRoleAsync("Users.Create", "admin", ct),
                AbortToken
            );
        }

        // then every pooled context, the one that borrowed the unit's connection included, reads on its own
        for (var i = 0; i < 3; i++)
        {
            (await repository.GetListAsync("Role", "admin", AbortToken))
                .Should()
                .ContainSingle()
                .Which.IsGranted.Should()
                .BeTrue();
        }
    }

    [Fact]
    public async Task should_refuse_a_raw_ado_unit_when_the_context_retries_outside_a_strategy()
    {
        // given
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new NpgsqlConnection(Fixture.SqlConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var unit = await factory.BeginAsync(connection, AbortToken);

        // when
        var act = () => unit.Permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*RunAsync(db, …)*");
        unit.State.Should().Be(UnitOfWorkState.Active);
    }

    protected override void AddPermissionsDbContextFactory(IServiceCollection services)
    {
        services.AddPooledDbContextFactory<PooledPermissionsDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString, npgsql => npgsql.EnableRetryOnFailure())
        );
    }

    protected override void UsePermissionsEntityFramework(HeadlessPermissionsSetupBuilder setup)
    {
        setup.UseEntityFramework<PooledPermissionsDbContext>();
    }

    private IHost _CreateHost()
    {
        return CreateHost(b =>
            b.Services.AddPermissionDefinitionProvider<PermissionsUnitOfWorkTests.UsersPermissionDefinitionProvider>()
        );
    }

    public sealed class PooledPermissionsDbContext(DbContextOptions<PooledPermissionsDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigureHeadlessPermissions(
                new PermissionsStorageOptions(),
                HeadlessStorageNaming.ForProvider(Database.ProviderName)
            );
        }
    }
}
