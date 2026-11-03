// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Grants;

/// <summary>
/// The EF grant repository over a plain <see cref="DbContext" />, which has no tenant query filter: the same role name
/// grants in several tenants and for the host, and each tenant must read and write only its own rows.
/// </summary>
public sealed class EfPermissionGrantRepositoryTenantTests : TestBase
{
    private const string _ProviderName = "Role";
    private const string _ProviderKey = "admin";
    private const string _Permission = "Users.Create";

    private readonly string _databasePath = Path.Combine(
        AppContext.BaseDirectory,
        $"permissions-tenants-{Guid.NewGuid():N}.db"
    );

    private readonly SqliteConnection _unitConnection;
    private readonly ITenantWriteGuardBypass _bypass = Substitute.For<ITenantWriteGuardBypass>();
    private readonly ServiceProvider _services;

    public EfPermissionGrantRepositoryTenantTests()
    {
        var connectionString = $"Data Source={_databasePath};Pooling=False";
        _unitConnection = new SqliteConnection(connectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddDbContextFactory<EfPermissionGrantRepositoryUnitOfWorkTests.GrantsDbContext>(options =>
            options.UseSqlite(connectionString)
        );
        services.AddHeadlessCaching(setup => setup.UseInMemory());
        // The dynamic definition store is off; its lock is never taken.
        services.AddSingleton(Substitute.For<IDistributedLock>());
        services.AddPermissionDefinitionProvider<DefinitionProvider>();
        services.AddSingleton(_bypass);
        services.AddHeadlessPermissions(setup =>
            setup.UseEntityFramework<EfPermissionGrantRepositoryUnitOfWorkTests.GrantsDbContext>()
        );
        _services = services.BuildServiceProvider();
    }

    private IPermissionGrantRepository Repository => _services.GetRequiredService<IPermissionGrantRepository>();

    private IPermissionGrantStore Store => _services.GetRequiredService<IPermissionGrantStore>();

    private ICurrentTenant CurrentTenant => _services.GetRequiredService<ICurrentTenant>();

    [Fact]
    public async Task should_read_only_the_current_tenants_rows_when_a_role_name_repeats_across_tenants()
    {
        // given the same grant target in two tenants and the host
        await _CreateSchemaAsync();
        await _InsertAsync("tenant-a", isGranted: true);
        await _InsertAsync("tenant-b", isGranted: false);
        await _InsertAsync(tenantId: null, isGranted: false);

        // when
        using (CurrentTenant.Change("tenant-a"))
        {
            var found = await Repository.FindAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
            var byTarget = await Repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken);
            var byNames = await Repository.GetListAsync([_Permission], _ProviderName, _ProviderKey, AbortToken);
            var byKeys = await Repository.GetListAsync([_Permission], _ProviderName, [_ProviderKey], AbortToken);

            // then
            found!.TenantId.Should().Be("tenant-a");
            byTarget.Should().ContainSingle().Which.TenantId.Should().Be("tenant-a");
            byNames.Should().ContainSingle().Which.TenantId.Should().Be("tenant-a");
            byKeys.Should().ContainSingle().Which.TenantId.Should().Be("tenant-a");
        }

        // and the host reads only the host's row
        var host = await Repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken);
        host.Should().ContainSingle().Which.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task should_read_only_the_current_tenants_rows_inside_a_unit()
    {
        // given
        await _CreateSchemaAsync();
        await _InsertAsync("tenant-a", isGranted: true);
        await _InsertAsync("tenant-b", isGranted: true);
        await using var owner = await _CreateOwnerAsync();
        var factory = _services.GetRequiredService<IUnitOfWorkFactory>();

        // when
        await using var unit = await factory.BeginAsync(owner, AbortToken);
        using var _ = CurrentTenant.Change("tenant-b");
        var byTarget = await Repository.GetListEnlistedAsync(unit, _ProviderName, _ProviderKey, AbortToken);
        var byNames = await Repository.GetListEnlistedAsync(
            unit,
            [_Permission],
            _ProviderName,
            _ProviderKey,
            AbortToken
        );

        // then
        byTarget.Should().ContainSingle().Which.TenantId.Should().Be("tenant-b");
        byNames.Should().ContainSingle().Which.TenantId.Should().Be("tenant-b");
    }

    [Fact]
    public async Task should_not_report_another_tenants_grant_to_a_permission_check()
    {
        // given only tenant B grants the permission to its admin role
        await _CreateSchemaAsync();
        await _InsertAsync("tenant-b", isGranted: true);

        // when tenant A checks its own admin role
        PermissionGrantStatus status;

        using (CurrentTenant.Change("tenant-a"))
        {
            status = await Store.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        }

        // then
        status.Should().Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_answer_a_permission_check_when_two_tenants_grant_the_same_permission_to_one_role()
    {
        // given
        await _CreateSchemaAsync();
        await _InsertAsync("tenant-a", isGranted: true);
        await _InsertAsync("tenant-b", isGranted: true);

        // when the host and a third tenant check the shared role name
        var host = await Store.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        PermissionGrantStatus other;

        using (CurrentTenant.Change("tenant-c"))
        {
            other = await Store.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        }

        // then
        host.Should().Be(PermissionGrantStatus.Undefined);
        other.Should().Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_write_this_tenants_grant_when_another_tenant_already_holds_it()
    {
        // given
        await _CreateSchemaAsync();
        await _InsertAsync("tenant-b", isGranted: true);

        // when
        using (CurrentTenant.Change("tenant-a"))
        {
            await Store.GrantAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        }

        // then
        (await _RowsAsync("tenant-a"))
            .Should()
            .ContainSingle()
            .Which.IsGranted.Should()
            .BeTrue();
        (await _RowsAsync("tenant-b")).Should().ContainSingle().Which.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_another_tenants_denial_when_granting_in_this_tenant()
    {
        // given
        await _CreateSchemaAsync();
        await _InsertAsync("tenant-b", isGranted: false);

        // when
        using (CurrentTenant.Change("tenant-a"))
        {
            await Store.GrantAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        }

        // then
        (await _RowsAsync("tenant-a"))
            .Should()
            .ContainSingle()
            .Which.IsGranted.Should()
            .BeTrue();
        (await _RowsAsync("tenant-b")).Should().ContainSingle().Which.IsGranted.Should().BeFalse();
    }

    [Fact]
    public async Task should_keep_another_tenants_grant_when_revoking_in_this_tenant()
    {
        // given
        await _CreateSchemaAsync();
        await _InsertAsync("tenant-b", isGranted: true);

        // when
        using (CurrentTenant.Change("tenant-a"))
        {
            await Store.RevokeAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        }

        // then
        (await _RowsAsync("tenant-a"))
            .Should()
            .ContainSingle()
            .Which.IsGranted.Should()
            .BeFalse();
        (await _RowsAsync("tenant-b")).Should().ContainSingle().Which.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task should_replace_this_tenants_grant_with_a_denial_when_revoking_it()
    {
        // given
        await _CreateSchemaAsync();
        var grant = await _InsertAsync("tenant-a", isGranted: true);

        // when
        using (CurrentTenant.Change("tenant-a"))
        {
            await Store.RevokeAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        }

        // then
        var row = (await _RowsAsync("tenant-a")).Should().ContainSingle().Subject;
        row.IsGranted.Should().BeFalse();
        row.Id.Should().NotBe(grant.Id);
    }

    [Fact]
    public async Task should_keep_the_deleted_record_when_the_insert_saved_with_it_fails()
    {
        // given a grant to flip, and another row whose id the replacement reuses so its insert fails
        await _CreateSchemaAsync();
        var grant = await _InsertAsync("tenant-a", isGranted: true);
        var other = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Other",
            _ProviderName,
            _ProviderKey,
            true,
            "tenant-a"
        );
        var clashing = new PermissionGrantRecord(other.Id, _Permission, _ProviderName, _ProviderKey, false, "tenant-a");

        using (CurrentTenant.Change("tenant-a"))
        {
            await Repository.InsertAsync(other, AbortToken);

            // when
            var save = () => Repository.SaveAsync([clashing], [grant], AbortToken);

            // then the insert fails, and the delete committed with it is rolled back
            await save.Should().ThrowAsync<DbUpdateException>();
        }

        var rows = await _RowsAsync("tenant-a");
        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(x => x.Id == grant.Id).Which.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task should_leave_another_tenants_record_when_deleting_it_from_this_tenant()
    {
        // given
        await _CreateSchemaAsync();
        var other = await _InsertAsync("tenant-b", isGranted: true);

        // when
        using (CurrentTenant.Change("tenant-a"))
        {
            await Repository.DeleteAsync(other, AbortToken);
        }

        // then
        (await _RowsAsync("tenant-b"))
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task should_leave_another_tenants_record_when_a_unit_deletes_it_from_this_tenant()
    {
        // given
        await _CreateSchemaAsync();
        var other = await _InsertAsync("tenant-b", isGranted: true);
        await using var owner = await _CreateOwnerAsync();
        var factory = _services.GetRequiredService<IUnitOfWorkFactory>();

        // when
        await using (var unit = await factory.BeginAsync(owner, AbortToken))
        {
            using (CurrentTenant.Change("tenant-a"))
            {
                await Repository.DeleteManyEnlistedAsync(unit, [other], AbortToken);
            }

            await unit.CompleteAsync(AbortToken);
        }

        // then
        (await _RowsAsync("tenant-b"))
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task should_open_the_write_guard_bypass_only_for_host_writes_without_an_ambient_tenant()
    {
        // given
        await _CreateSchemaAsync();
        var hostScope = Substitute.For<IDisposable>();
        _bypass.BeginBypass().Returns(hostScope);

        // when the host writes and deletes a host row
        var hostGrant = new PermissionGrantRecord(Guid.NewGuid(), _Permission, _ProviderName, _ProviderKey, true);
        await Repository.InsertAsync(hostGrant, AbortToken);
        await Repository.DeleteAsync(hostGrant, AbortToken);

        // then both writes ran inside one bypass scope each
        _bypass.Received(2).BeginBypass();
        hostScope.Received(2).Dispose();
        _bypass.ClearReceivedCalls();

        // when a write names a tenant, with or without that tenant ambient
        var tenantGrant = new PermissionGrantRecord(
            Guid.NewGuid(),
            _Permission,
            _ProviderName,
            _ProviderKey,
            true,
            "t1"
        );
        using (CurrentTenant.Change("t1"))
        {
            await Repository.InsertManyAsync([tenantGrant], AbortToken);
            await Repository.DeleteManyAsync([tenantGrant], AbortToken);
        }

        var tenantRowFromHost = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Other",
            _ProviderName,
            _ProviderKey,
            true,
            "t2"
        );
        await Repository.InsertAsync(tenantRowFromHost, AbortToken);

        // then the guard stays closed
        _bypass.DidNotReceive().BeginBypass();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await _unitConnection.DisposeAsync();
        File.Delete(_databasePath);
        await base.DisposeAsyncCore();
    }

    private async Task _CreateSchemaAsync()
    {
        var dbFactory = _services.GetRequiredService<
            IDbContextFactory<EfPermissionGrantRepositoryUnitOfWorkTests.GrantsDbContext>
        >();
        await using var db = await dbFactory.CreateDbContextAsync(AbortToken);
        await db.Database.EnsureCreatedAsync(AbortToken);
    }

    private async Task<EfPermissionGrantRepositoryUnitOfWorkTests.GrantsDbContext> _CreateOwnerAsync()
    {
        await _unitConnection.OpenAsync(AbortToken);

        return new EfPermissionGrantRepositoryUnitOfWorkTests.GrantsDbContext(
            new DbContextOptionsBuilder<EfPermissionGrantRepositoryUnitOfWorkTests.GrantsDbContext>()
                .UseSqlite(_unitConnection)
                .Options
        );
    }

    private async Task<PermissionGrantRecord> _InsertAsync(string? tenantId, bool isGranted)
    {
        var record = new PermissionGrantRecord(
            Guid.NewGuid(),
            _Permission,
            _ProviderName,
            _ProviderKey,
            isGranted,
            tenantId
        );

        using (CurrentTenant.Change(tenantId))
        {
            await Repository.InsertAsync(record, AbortToken);
        }

        return record;
    }

    private async Task<List<PermissionGrantRecord>> _RowsAsync(string tenantId)
    {
        var dbFactory = _services.GetRequiredService<
            IDbContextFactory<EfPermissionGrantRepositoryUnitOfWorkTests.GrantsDbContext>
        >();
        await using var db = await dbFactory.CreateDbContextAsync(AbortToken);

        return await db.Set<PermissionGrantRecord>()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(AbortToken);
    }

    private sealed class DefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("Users").AddChild(_Permission);
        }
    }
}
