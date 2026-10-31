// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.DistributedLocks;
using Headless.Settings;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// <c>unit.Settings</c> on every raw-ADO settings provider: a write commits or rolls back with the caller's unit, the
/// value cache keeps its pre-commit value until the unit commits, the inherited-value comparison reads a fallback the
/// unit already wrote, and a unit on another database is refused before any write.
/// </summary>
public abstract class SettingsUnitOfWorkConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ISettingsStorageFixture
{
    private const string _Schema = "settings_uow";
    private const string _Setting = "App.Theme";

    [Fact]
    public async Task should_commit_with_the_unit_and_clear_the_warm_cache_only_after_commit()
    {
        // given a warm cache that says the global value is unset
        using var host = await _StartHostAsync();
        var manager = host.Services.GetRequiredService<ISettingManager>();
        (await _GlobalAsync(manager)).Should().BeNull();
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when
        await using (var unit = await _BeginAsync(host, connection))
        {
            await unit.Settings.SetGlobalAsync(_Setting, "dark", AbortToken);

            // then an owned unit stays replayable, and the cache still holds the pre-commit value
            unit.IsRetryPrevented.Should().BeFalse();
            (await _GlobalAsync(manager)).Should().BeNull();

            await unit.CompleteAsync(AbortToken);
        }

        // and the committed value is read once the unit committed
        (await _GlobalAsync(manager))
            .Should()
            .Be("dark");
    }

    [Fact]
    public async Task should_discard_the_write_when_the_unit_rolls_back()
    {
        // given
        using var host = await _StartHostAsync();
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when
        await using (var unit = await _BeginAsync(host, connection))
        {
            await unit.Settings.SetGlobalAsync(_Setting, "dark", AbortToken);
            await unit.RollbackAsync();
        }

        // then
        (await _StoredAsync(host, SettingValueProviderNames.Global, null))
            .Should()
            .BeEmpty();
        (await _GlobalAsync(host.Services.GetRequiredService<ISettingManager>())).Should().BeNull();
    }

    [Fact]
    public async Task should_compare_against_a_fallback_the_same_unit_wrote()
    {
        // given
        using var host = await _StartHostAsync();
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when one unit writes the global value, then the same value for a tenant
        await using (var unit = await _BeginAsync(host, connection))
        {
            await unit.Settings.SetGlobalAsync(_Setting, "dark", AbortToken);
            await unit.Settings.SetForTenantAsync("t1", _Setting, "dark", cancellationToken: AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then the tenant inherits instead of storing a copy
        (await _StoredAsync(host, SettingValueProviderNames.Global, null))
            .Should()
            .ContainSingle();
        (await _StoredAsync(host, SettingValueProviderNames.Tenant, "t1")).Should().BeEmpty();
    }

    [Fact]
    public async Task should_delete_a_scope_with_the_unit_and_clear_its_cache_after_commit()
    {
        // given a committed tenant value the cache already holds
        using var host = await _StartHostAsync();
        var manager = host.Services.GetRequiredService<ISettingManager>();
        await manager.SetForTenantAsync("t1", _Setting, "light", cancellationToken: AbortToken);
        (await _TenantAsync(manager)).Should().Be("light");
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when
        await using (var unit = await _BeginAsync(host, connection))
        {
            await unit.Settings.DeleteAsync(SettingValueProviderNames.Tenant, "t1", AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then
        (await _StoredAsync(host, SettingValueProviderNames.Tenant, "t1"))
            .Should()
            .BeEmpty();
        (await _TenantAsync(manager)).Should().BeNull();
    }

    [Fact]
    public async Task should_mark_an_observed_unit_not_replayable_and_commit_with_the_callers_transaction()
    {
        // given a transaction the caller commits itself
        using var host = await _StartHostAsync();
        await using var connection = await _OpenAsync(fixture.ConnectionString);
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        await using var unit = fixture.EnlistUnitOfWork(
            host.Services.GetRequiredService<IUnitOfWorkFactory>(),
            connection,
            transaction
        );

        // when
        await unit.Settings.SetGlobalAsync(_Setting, "dark", AbortToken);
        await transaction.CommitAsync(AbortToken);
        await unit.CompleteAsync(AbortToken);

        // then
        unit.IsRetryPrevented.Should().BeTrue();
        (await _StoredAsync(host, SettingValueProviderNames.Global, null)).Should().ContainSingle();
    }

    [Fact]
    public async Task should_refuse_a_unit_on_another_database_before_writing()
    {
        // given
        using var host = await _StartHostAsync();
        await using var connection = await _OpenAsync(fixture.OtherDatabaseConnectionString);
        await using var unit = await _BeginAsync(host, connection);

        // when
        var act = () => unit.Settings.SetGlobalAsync(_Setting, "dark", AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*configured for database*");
        unit.State.Should().Be(UnitOfWorkState.Active);
        (await _StoredAsync(host, SettingValueProviderNames.Global, null)).Should().BeEmpty();
    }

    private async Task<IHost> _StartHostAsync()
    {
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var host = fixture.CreateHost(
            _Schema,
            configureServices: services =>
            {
                services.AddSettingDefinitionProvider<ThemeSettingDefinitionProvider>();
                // The definition manager takes a lock only for the dynamic store, which these hosts leave off.
                services.AddSingleton(Substitute.For<IDistributedLock>());
                // The User provider takes the current user; these tests write Global and Tenant values only.
                services.AddSingleton(Substitute.For<ICurrentUser>());
            }
        );
        await host.StartAsync(AbortToken);

        return host;
    }

    private async Task<System.Data.Common.DbConnection> _OpenAsync(string connectionString)
    {
        var connection = fixture.CreateConnection(connectionString);
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    private ValueTask<IUnitOfWork> _BeginAsync(IHost host, System.Data.Common.DbConnection connection)
    {
        return fixture.BeginUnitOfWorkAsync(
            host.Services.GetRequiredService<IUnitOfWorkFactory>(),
            connection,
            AbortToken
        );
    }

    private async Task<string?> _GlobalAsync(ISettingManager manager)
    {
        var value = await manager.GetAsync(
            _Setting,
            SettingValueProviderNames.Global,
            providerKey: null,
            fallback: false,
            AbortToken
        );

        return value.Value;
    }

    private async Task<string?> _TenantAsync(ISettingManager manager)
    {
        var value = await manager.GetAsync(
            _Setting,
            SettingValueProviderNames.Tenant,
            "t1",
            fallback: false,
            AbortToken
        );

        return value.Value;
    }

    private Task<List<SettingValueRecord>> _StoredAsync(IHost host, string providerName, string? providerKey)
    {
        return host
            .Services.GetRequiredService<ISettingValueRecordRepository>()
            .GetListAsync(providerName, providerKey, AbortToken);
    }

    [UsedImplicitly]
    private sealed class ThemeSettingDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add(new SettingDefinitionCreateOptions { Name = _Setting });
        }
    }
}
