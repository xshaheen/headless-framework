// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Permissions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsUnitOfWorkTests(SqlServerPermissionsFixture fixture) : TestBase
{
    private const string _Schema = "permissions_sql_uow";

    [Fact]
    public async Task should_commit_a_grant_with_the_unit_and_clear_the_warm_cache_only_after_commit()
    {
        // given a warm cache that says the permission is undefined
        using var host = await _StartHostAsync();
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();
        (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Undefined);
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // when
        await using (var unit = await factory.BeginAsync(connection, AbortToken))
        {
            await unit.Permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);

            // then an owned unit stays replayable: its block re-runs the write
            unit.IsRetryPrevented.Should().BeFalse();

            // and the cache still holds the pre-commit value while the unit is open
            (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
                .Should()
                .Be(PermissionGrantStatus.Undefined);

            await unit.CompleteAsync(AbortToken);
        }

        // and the committed grant is read once the unit committed
        (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Granted);
        (await _CountGrantsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_discard_the_grant_and_keep_the_cache_when_the_unit_rolls_back()
    {
        // given
        using var host = await _StartHostAsync();
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // when
        await using (var unit = await factory.BeginAsync(connection, AbortToken))
        {
            await unit.Permissions.SetAsync(["Users.Create", "Users.Delete"], "Role", "admin", true, AbortToken);
            await unit.RollbackAsync();
        }

        // then
        (await _CountGrantsAsync())
            .Should()
            .Be(0);
        (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_replace_a_denial_and_delete_a_subject_inside_one_unit()
    {
        // given a committed denial
        using var host = await _StartHostAsync();
        var manager = host.Services.GetRequiredService<IPermissionManager>();
        await manager.RevokeFromRoleAsync("Users.Create", "admin", AbortToken);
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        // when the unit flips it to a grant, then commits
        await using (var unit = await factory.BeginAsync(connection, AbortToken))
        {
            await unit.Permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        var afterGrant = await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken);

        // and a second unit deletes the subject
        await using (var unit = await factory.BeginAsync(connection, AbortToken))
        {
            await unit.Permissions.DeleteAsync("Role", "admin", AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then
        afterGrant.Should().Be(PermissionGrantStatus.Granted);
        (await _CountGrantsAsync()).Should().Be(0);
        (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_mark_an_observed_unit_not_replayable_and_commit_with_the_callers_transaction()
    {
        // given a transaction the caller commits itself
        using var host = await _StartHostAsync();
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);
        await using var unit = factory.Enlist(connection, transaction);

        // when
        await unit.Permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);
        await transaction.CommitAsync(AbortToken);
        await unit.CompleteAsync(AbortToken);

        // then
        unit.IsRetryPrevented.Should().BeTrue();
        (await _CountGrantsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_refuse_a_unit_on_another_database_before_writing()
    {
        // given
        using var host = await _StartHostAsync();
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        var otherDatabase = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = "tempdb" };
        await using var connection = new SqlConnection(otherDatabase.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var unit = await factory.BeginAsync(connection, AbortToken);

        // when
        var act = () => unit.Permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*database 'tempdb'*");
        unit.State.Should().Be(UnitOfWorkState.Active);
        (await _CountGrantsAsync()).Should().Be(0);
    }

    private async Task<IHost> _StartHostAsync()
    {
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await using var command = new SqlCommand(
                $"""
                IF OBJECT_ID(N'{_Schema}.PermissionGrants', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionGrants];
                IF OBJECT_ID(N'{_Schema}.PermissionDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionDefinitions];
                IF OBJECT_ID(N'{_Schema}.PermissionGroupDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[PermissionGroupDefinitions];
                IF OBJECT_ID(N'{_Schema}.headless_schema_history', N'U') IS NOT NULL DROP TABLE [{_Schema}].[headless_schema_history];
                IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'DROP SCHEMA [{_Schema}]');
                """,
                connection
            );
            await command.ExecuteNonQueryAsync(AbortToken);
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        // The definition manager takes a lock only for the dynamic store, which these hosts leave off.
        builder.Services.AddSingleton(Substitute.For<IDistributedLock>());
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        builder.Services.AddPermissionDefinitionProvider<UsersPermissionDefinitionProvider>();
        builder.Services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UseSqlServer(fixture.ConnectionString);
        });
        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);

        var host = builder.Build();
        await host.StartAsync(AbortToken);

        return host;
    }

    private async Task<long> _CountGrantsAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"SELECT COUNT_BIG(*) FROM [{_Schema}].[PermissionGrants];",
            connection
        );

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    [UsedImplicitly]
    private sealed class UsersPermissionDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            var users = context.AddGroup("Users");
            users.AddChild("Users.Create");
            users.AddChild("Users.Delete");
        }
    }
}
