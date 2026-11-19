// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.DistributedLocks;
using Headless.Permissions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// <c>unit.Permissions</c> on every raw-ADO permissions provider: a grant, denial, or subject delete commits or rolls
/// back with the caller's unit, the grant cache keeps its pre-commit value until the unit commits, an observed unit
/// is not replayable, and a unit on another database is refused before any write.
/// </summary>
public abstract class PermissionsUnitOfWorkConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IPermissionsStorageFixture
{
    private const string _Schema = "permissions_uow";

    [Fact]
    public async Task should_commit_a_grant_with_the_unit_and_clear_the_warm_cache_only_after_commit()
    {
        // given a warm cache that says the permission is undefined
        using var host = await _StartHostAsync();
        var store = host.Services.GetRequiredService<IPermissionGrantStore>();
        (await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Undefined);
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when
        await using (var unit = await _BeginAsync(host, connection))
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
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when
        await using (var unit = await _BeginAsync(host, connection))
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
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when the unit flips it to a grant, then commits
        await using (var unit = await _BeginAsync(host, connection))
        {
            await unit.Permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        var afterGrant = await store.IsGrantedAsync("Users.Create", "Role", "admin", AbortToken);

        // and a second unit deletes the subject
        await using (var unit = await _BeginAsync(host, connection))
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
        await using var connection = await _OpenAsync(fixture.ConnectionString);
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        await using var unit = fixture.EnlistUnitOfWork(factory, connection, transaction);

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
        await using var connection = await _OpenAsync(fixture.OtherDatabaseConnectionString);
        await using var unit = await _BeginAsync(host, connection);

        // when
        var act = () => unit.Permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*database '{connection.Database}'*");
        unit.State.Should().Be(UnitOfWorkState.Active);
        (await _CountGrantsAsync()).Should().Be(0);
    }

    private async Task<IHost> _StartHostAsync()
    {
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var host = fixture.CreateHost(
            _Schema,
            configureServices: services =>
            {
                // The definition manager takes a lock only for the dynamic store, which these hosts leave off.
                services.AddSingleton(Substitute.For<IDistributedLock>());
                services.AddPermissionDefinitionProvider<UsersPermissionDefinitionProvider>();
            }
        );
        await host.StartAsync(AbortToken);

        return host;
    }

    private async Task<DbConnection> _OpenAsync(string connectionString)
    {
        var connection = fixture.CreateConnection(connectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    private ValueTask<IUnitOfWork> _BeginAsync(IHost host, DbConnection connection)
    {
        return fixture.BeginUnitOfWorkAsync(
            host.Services.GetRequiredService<IUnitOfWorkFactory>(),
            connection,
            AbortToken
        );
    }

    private Task<long> _CountGrantsAsync()
    {
        return fixture.CountRowsAsync(_Schema, fixture.GrantsTable(), AbortToken);
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
