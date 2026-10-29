// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Security.Claims;
using Headless;
using Headless.Context;
using Headless.DistributedLocks;
using Headless.Features;
using Headless.Security;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// <c>unit.Features</c> on every raw-ADO features provider: a write commits or rolls back with the caller's unit, the
/// value cache keeps its pre-commit value until the unit commits, the inherited-value comparison reads a fallback the
/// unit already wrote, and a unit on another database is refused before any write.
/// </summary>
public abstract class FeaturesUnitOfWorkConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IFeaturesStorageFixture
{
    private const string _Schema = "features_uow";
    private const string _Feature = "Reports.Seats";
    private const string _Edition = "pro";

    [Fact]
    public async Task should_commit_with_the_unit_and_clear_the_warm_cache_only_after_commit()
    {
        // given a warm cache that says the tenant has no value
        using var host = await _StartHostAsync();
        var manager = host.Services.GetRequiredService<IFeatureManager>();
        (await _TenantAsync(manager)).Should().BeNull();
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when
        await using (var unit = await _BeginAsync(host, connection))
        {
            await unit.Features.SetForTenantAsync(_Feature, "10", "t1", cancellationToken: AbortToken);

            // then an owned unit stays replayable, and the cache still holds the pre-commit value
            unit.IsRetryPrevented.Should().BeFalse();
            (await _TenantAsync(manager)).Should().BeNull();

            await unit.CompleteAsync(AbortToken);
        }

        // and the committed value is read once the unit committed
        (await _TenantAsync(manager))
            .Should()
            .Be("10");
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
            await unit.Features.SetForTenantAsync(_Feature, "10", "t1", cancellationToken: AbortToken);
            await unit.RollbackAsync();
        }

        // then
        (await _StoredAsync(host, FeatureValueProviderNames.Tenant, "t1"))
            .Should()
            .BeEmpty();
        (await _TenantAsync(host.Services.GetRequiredService<IFeatureManager>())).Should().BeNull();
    }

    [Fact]
    public async Task should_compare_against_a_fallback_the_same_unit_wrote()
    {
        // given
        using var host = await _StartHostAsync();
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when one unit writes the edition value, then the same value for a tenant
        await using (var unit = await _BeginAsync(host, connection))
        {
            await unit.Features.SetForEditionAsync(_Feature, "25", _Edition, cancellationToken: AbortToken);
            await unit.Features.SetForTenantAsync(_Feature, "25", "t1", cancellationToken: AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then the tenant inherits instead of storing a copy
        (await _StoredAsync(host, FeatureValueProviderNames.Edition, _Edition))
            .Should()
            .ContainSingle();
        (await _StoredAsync(host, FeatureValueProviderNames.Tenant, "t1")).Should().BeEmpty();
    }

    [Fact]
    public async Task should_delete_a_scope_with_the_unit_and_clear_its_cache_after_commit()
    {
        // given a committed tenant value the cache already holds
        using var host = await _StartHostAsync();
        var manager = host.Services.GetRequiredService<IFeatureManager>();
        await manager.SetForTenantAsync(_Feature, "10", "t1");
        (await _TenantAsync(manager)).Should().Be("10");
        await using var connection = await _OpenAsync(fixture.ConnectionString);

        // when
        await using (var unit = await _BeginAsync(host, connection))
        {
            await unit.Features.DeleteAsync(FeatureValueProviderNames.Tenant, "t1", AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then
        (await _StoredAsync(host, FeatureValueProviderNames.Tenant, "t1"))
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
        await unit.Features.SetForTenantAsync(_Feature, "10", "t1", cancellationToken: AbortToken);
        await transaction.CommitAsync(AbortToken);
        await unit.CompleteAsync(AbortToken);

        // then
        unit.IsRetryPrevented.Should().BeTrue();
        (await _StoredAsync(host, FeatureValueProviderNames.Tenant, "t1")).Should().ContainSingle();
    }

    [Fact]
    public async Task should_refuse_a_unit_on_another_database_before_writing()
    {
        // given
        using var host = await _StartHostAsync();
        await using var connection = await _OpenAsync(fixture.OtherDatabaseConnectionString);
        await using var unit = await _BeginAsync(host, connection);

        // when
        var act = () => unit.Features.SetForTenantAsync(_Feature, "10", "t1", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*configured for database*");
        unit.State.Should().Be(UnitOfWorkState.Active);
        (await _StoredAsync(host, FeatureValueProviderNames.Tenant, "t1")).Should().BeEmpty();
    }

    private async Task<IHost> _StartHostAsync()
    {
        await fixture.DropSchemaAsync(_Schema, AbortToken);
        var principal = Substitute.For<ICurrentPrincipalAccessor>();
        principal.Principal.Returns(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(UserClaimTypes.EditionId, _Edition)]))
        );
        var host = fixture.CreateHost(
            _Schema,
            configureServices: services =>
            {
                services.AddFeatureDefinitionProvider<SeatsFeatureDefinitionProvider>();
                // The Edition provider reads the edition from the current principal when the key is left out.
                services.AddSingleton(principal);
                // The definition manager takes a lock only for the dynamic store, which these hosts leave off.
                services.AddSingleton(Substitute.For<IDistributedLock>());
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

    private async Task<string?> _TenantAsync(IFeatureManager manager)
    {
        var value = await manager.GetAsync(
            _Feature,
            FeatureValueProviderNames.Tenant,
            "t1",
            fallback: false,
            AbortToken
        );

        return value.Value;
    }

    private Task<List<FeatureValueRecord>> _StoredAsync(IHost host, string providerName, string? providerKey)
    {
        return host
            .Services.GetRequiredService<IFeatureValueRecordRepository>()
            .GetListAsync(providerName, providerKey, AbortToken);
    }

    [UsedImplicitly]
    private sealed class SeatsFeatureDefinitionProvider : IFeatureDefinitionProvider
    {
        public void Define(IFeatureDefinitionContext context)
        {
            context.AddGroup("Reports").AddChild(new FeatureDefinitionCreateOptions { Name = _Feature });
        }
    }
}
