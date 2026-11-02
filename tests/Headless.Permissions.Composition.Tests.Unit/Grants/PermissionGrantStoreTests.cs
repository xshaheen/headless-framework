// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Grants;

public sealed class PermissionGrantStoreTests : TestBase
{
    private const string _ProviderName = "Role";
    private const string _ProviderKey = "admin";

    private readonly IPermissionDefinitionManager _definitionManager;
    private readonly IPermissionGrantRepository _repository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICache<PermissionGrantCacheItem> _cache;
    private readonly ICurrentTenant _currentTenant;
    private readonly PermissionGrantStore _sut;

    public PermissionGrantStoreTests()
    {
        _definitionManager = Substitute.For<IPermissionDefinitionManager>();
        _repository = Substitute.For<IPermissionGrantRepository>();
        _guidGenerator = Substitute.For<IGuidGenerator>();
        _cache = Substitute.For<ICache<PermissionGrantCacheItem>>();
        _currentTenant = Substitute.For<ICurrentTenant>();
        var logger = Substitute.For<ILogger<PermissionGrantStore>>();

        _sut = new PermissionGrantStore(
            _definitionManager,
            _repository,
            _guidGenerator,
            _cache,
            _currentTenant,
            Options.Create(new PermissionManagementOptions()),
            logger
        );
    }

    #region IsGrantedAsync - Single

    [Fact]
    public async Task should_return_status_from_warm_cache()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var cachedItem = new PermissionGrantCacheItem(isGranted: true);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(cachedItem, hasValue: true));

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Granted);
        await _repository.DidNotReceive().GetListAsync(_ProviderName, _ProviderKey, AbortToken);
    }

    [Fact]
    public async Task should_warm_cache_on_miss_and_return_status()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var permission = _CreatePermission(permissionName);
        var grantRecord = new PermissionGrantRecord(Guid.NewGuid(), permissionName, _ProviderName, _ProviderKey, true);

        _cache.GetAsync(cacheKey, AbortToken).Returns(CacheValue<PermissionGrantCacheItem>.NoValue);
        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission]);
        _repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken).Returns([grantRecord]);

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Granted);
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(static d => d.Count == 1),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_reload_every_grant_into_cache_on_refresh()
    {
        // given
        var granted = _CreatePermission("Users.Create");
        var revoked = _CreatePermission("Users.Delete");
        var undefined = _CreatePermission("Users.Read");
        _definitionManager.GetPermissionsAsync(AbortToken).Returns([granted, revoked, undefined]);
        _repository
            .GetListAsync(_ProviderName, _ProviderKey, AbortToken)
            .Returns([
                new PermissionGrantRecord(Guid.NewGuid(), granted.Name, _ProviderName, _ProviderKey, true),
                new PermissionGrantRecord(Guid.NewGuid(), revoked.Name, _ProviderName, _ProviderKey, false),
            ]);

        // when
        await _sut.RefreshAsync(_ProviderName, _ProviderKey, AbortToken);

        // then
        await _cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(d =>
                    d.Count == 3
                    && d[
                        PermissionGrantCacheItem.CalculateCacheKey(granted.Name, _ProviderName, _ProviderKey)
                    ].IsGranted == true
                    && d[
                        PermissionGrantCacheItem.CalculateCacheKey(revoked.Name, _ProviderName, _ProviderKey)
                    ].IsGranted == false
                    && d[
                        PermissionGrantCacheItem.CalculateCacheKey(undefined.Name, _ProviderName, _ProviderKey)
                    ].IsGranted == null
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_return_undefined_when_no_record_exists()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var permission = _CreatePermission(permissionName);

        _cache.GetAsync(cacheKey, AbortToken).Returns(CacheValue<PermissionGrantCacheItem>.NoValue);
        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission]);
        _repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken).Returns([]);

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_return_prohibited_for_explicit_denial()
    {
        // given
        const string permissionName = "Users.Delete";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var cachedItem = new PermissionGrantCacheItem(isGranted: false);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(cachedItem, hasValue: true));

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_return_undefined_for_null_cached_value()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var cachedItem = new PermissionGrantCacheItem(isGranted: null);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(cachedItem, hasValue: true));

        // when
        var result = await _sut.IsGrantedAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().Be(PermissionGrantStatus.Undefined);
    }

    #endregion IsGrantedAsync - Single

    #region IsGrantedAsync - Batch

    [Fact]
    public async Task should_optimize_single_item_batch()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var cachedItem = new PermissionGrantCacheItem(isGranted: true);

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(cachedItem, hasValue: true));

        // when
        var result = await _sut.IsGrantedAsync([permissionName], _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().ContainSingle();
        result[permissionName].Should().Be(PermissionGrantStatus.Granted);
        await _cache.Received(1).GetAsync(cacheKey, AbortToken);
        await _cache.DidNotReceive().GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken);
    }

    [Fact]
    public async Task should_batch_check_multiple_permissions()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var cacheKey1 = PermissionGrantCacheItem.CalculateCacheKey("Users.Create", _ProviderName, _ProviderKey);
        var cacheKey2 = PermissionGrantCacheItem.CalculateCacheKey("Users.Update", _ProviderName, _ProviderKey);

        var cacheResults = new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal)
        {
            [cacheKey1] = new(new PermissionGrantCacheItem(true), true),
            [cacheKey2] = new(new PermissionGrantCacheItem(false), true),
        };

        _cache.GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken).Returns(cacheResults);

        // when
        var result = await _sut.IsGrantedAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        result["Users.Create"].Should().Be(PermissionGrantStatus.Granted);
        result["Users.Update"].Should().Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_warm_cache_for_missing_items_in_batch()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var cacheKey1 = PermissionGrantCacheItem.CalculateCacheKey("Users.Create", _ProviderName, _ProviderKey);
        var cacheKey2 = PermissionGrantCacheItem.CalculateCacheKey("Users.Update", _ProviderName, _ProviderKey);

        var cacheResults = new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal)
        {
            [cacheKey1] = new(new PermissionGrantCacheItem(true), true),
            [cacheKey2] = CacheValue<PermissionGrantCacheItem>.NoValue,
        };

        var permission2 = _CreatePermission("Users.Update");
        var grantRecord = new PermissionGrantRecord(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, true);

        _cache.GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken).Returns(cacheResults);
        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission2]);
        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([grantRecord]);

        // when
        var result = await _sut.IsGrantedAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        await _cache
            .Received(1)
            .UpsertAllAsync(Arg.Any<IDictionary<string, PermissionGrantCacheItem>>(), Arg.Any<TimeSpan>(), AbortToken);
    }

    #endregion IsGrantedAsync - Batch

    #region IsGrantedAsync - Batch across provider keys

    [Fact]
    public async Task should_serve_a_warm_multi_key_read_from_one_cache_call()
    {
        // given — both roles' entries are cached
        string[] permissionNames = ["Users.Create", "Users.Update"];
        string[] roles = ["admin", "manager"];

        var cacheResults = new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal);

        foreach (var role in roles)
        {
            foreach (var name in permissionNames)
            {
                cacheResults[PermissionGrantCacheItem.CalculateCacheKey(name, _ProviderName, role)] = new(
                    new PermissionGrantCacheItem(string.Equals(role, "admin", StringComparison.Ordinal)),
                    true
                );
            }
        }

        _cache.GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken).Returns(cacheResults);

        // when
        var result = await _sut.IsGrantedAsync(permissionNames, _ProviderName, roles, AbortToken);

        // then — one cache read answered every (name, role) pair, and the repository stayed untouched
        await _cache.Received(1).GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken);
        await _repository
            .DidNotReceive()
            .GetListAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
        result.Keys.Should().BeEquivalentTo("admin", "manager");
        result["admin"]["Users.Create"].Should().Be(PermissionGrantStatus.Granted);
        result["admin"]["Users.Update"].Should().Be(PermissionGrantStatus.Granted);
        result["manager"]["Users.Create"].Should().Be(PermissionGrantStatus.Prohibited);
        result["manager"]["Users.Update"].Should().Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_load_every_roles_misses_in_one_repository_call()
    {
        // given — nothing is cached
        string[] permissionNames = ["Users.Create", "Users.Update"];
        string[] roles = ["admin", "manager"];

        var permission1 = _CreatePermission("Users.Create");
        var permission2 = _CreatePermission("Users.Update");
        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission1, permission2]);

        _cache
            .GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken)
            .Returns(new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal));

        _repository
            .GetListAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                _ProviderName,
                Arg.Any<IReadOnlyCollection<string>>(),
                AbortToken
            )
            .Returns([
                new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", _ProviderName, "admin", true),
                new PermissionGrantRecord(Guid.NewGuid(), "Users.Update", _ProviderName, "admin", false),
                new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", _ProviderName, "manager", true),
            ]);

        // when
        var result = await _sut.IsGrantedAsync(permissionNames, _ProviderName, roles, AbortToken);

        // then — one repository read carried both roles' rows
        await _repository
            .Received(1)
            .GetListAsync(
                Arg.Is<IReadOnlyCollection<string>>(names => names.Order().SequenceEqual(permissionNames.Order())),
                _ProviderName,
                Arg.Is<IReadOnlyCollection<string>>(keys => keys.Order().SequenceEqual(roles.Order())),
                AbortToken
            );
        result["admin"]["Users.Create"].Should().Be(PermissionGrantStatus.Granted);
        result["admin"]["Users.Update"].Should().Be(PermissionGrantStatus.Prohibited);
        result["manager"]["Users.Create"].Should().Be(PermissionGrantStatus.Granted);
        result["manager"]["Users.Update"].Should().Be(PermissionGrantStatus.Undefined);

        // and — the misses of both roles were cached together
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(d =>
                    d.Count == 4
                    && d[PermissionGrantCacheItem.CalculateCacheKey("Users.Create", _ProviderName, "admin")].IsGranted
                        == true
                    && d[PermissionGrantCacheItem.CalculateCacheKey("Users.Update", _ProviderName, "admin")].IsGranted
                        == false
                    && d[PermissionGrantCacheItem.CalculateCacheKey("Users.Create", _ProviderName, "manager")].IsGranted
                        == true
                    && d[PermissionGrantCacheItem.CalculateCacheKey("Users.Update", _ProviderName, "manager")].IsGranted
                        == null
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_agree_with_the_single_key_read_per_pair()
    {
        // given — the single-key path resolves admin/Users.Create from a warm cache entry
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, "admin");

        _cache
            .GetAsync(cacheKey, AbortToken)
            .Returns(new CacheValue<PermissionGrantCacheItem>(new PermissionGrantCacheItem(true), true));

        _cache
            .GetAllAsync(Arg.Any<IEnumerable<string>>(), AbortToken)
            .Returns(
                new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal)
                {
                    [cacheKey] = new(new PermissionGrantCacheItem(true), true),
                }
            );

        // when
        var single = await _sut.IsGrantedAsync(permissionName, _ProviderName, "admin", AbortToken);
        var batch = await _sut.IsGrantedAsync([permissionName], _ProviderName, ["admin"], AbortToken);

        // then
        single.Should().Be(PermissionGrantStatus.Granted);
        batch["admin"][permissionName].Should().Be(single);
    }

    [Fact]
    public async Task should_refuse_padded_names_and_keys_in_a_multi_key_read()
    {
        // when
        var act = () => _sut.IsGrantedAsync(["Users.Create"], _ProviderName, ["admin "], AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<ArgumentException>().WithMessage("*starts or ends with white space*");
        _repository.ReceivedCalls().Should().BeEmpty();
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    #endregion IsGrantedAsync - Batch across provider keys

    #region GrantAsync - Single

    [Fact]
    public async Task should_insert_grant_when_no_record_exists()
    {
        // given
        const string permissionName = "Users.Create";
        var newId = Guid.NewGuid();
        const string tenantId = "tenant-1";

        _currentTenant.Id.Returns(tenantId);
        _repository
            .FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken)
            .Returns((PermissionGrantRecord?)null);
        _guidGenerator.Create().Returns(newId);

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository
            .Received(1)
            .InsertAsync(
                Arg.Is<PermissionGrantRecord>(r =>
                    r.Id == newId
                    && r.Name == permissionName
                    && r.ProviderName == _ProviderName
                    && r.ProviderKey == _ProviderKey
                    && r.IsGranted
                    && r.TenantId == tenantId
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_update_denied_record_to_granted()
    {
        // given
        const string permissionName = "Users.Create";
        var existingId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var deniedRecord = new PermissionGrantRecord(
            existingId,
            permissionName,
            _ProviderName,
            _ProviderKey,
            false,
            "tenant-1"
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(deniedRecord);
        _guidGenerator.Create().Returns(newId);

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository.Received(1).DeleteAsync(deniedRecord, AbortToken);
        await _repository.Received(1).InsertAsync(Arg.Is<PermissionGrantRecord>(static r => r.IsGranted), AbortToken);
    }

    [Fact]
    public async Task should_skip_when_already_granted()
    {
        // given
        const string permissionName = "Users.Create";
        var grantedRecord = new PermissionGrantRecord(
            Guid.NewGuid(),
            permissionName,
            _ProviderName,
            _ProviderKey,
            true
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(grantedRecord);

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _repository.DidNotReceive().InsertAsync(Arg.Any<PermissionGrantRecord>(), AbortToken);
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<PermissionGrantRecord>(), AbortToken);
    }

    [Fact]
    public async Task should_update_cache_on_grant()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);

        _repository
            .FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken)
            .Returns((PermissionGrantRecord?)null);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAsync(
                cacheKey,
                Arg.Is<PermissionGrantCacheItem>(static c => c.IsGranted == true),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_stamp_grant_with_the_current_tenant_it_looked_up_and_cached_under()
    {
        // given — the lookup and the cache are scoped to the current tenant, so the written record must be too
        const string permissionName = "Users.Create";

        _currentTenant.Id.Returns("tenant-a");
        _repository
            .FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken)
            .Returns((PermissionGrantRecord?)null);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionName, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _repository
            .Received(1)
            .InsertAsync(Arg.Is<PermissionGrantRecord>(static r => r.TenantId == "tenant-a"), AbortToken);
    }

    [Fact]
    public async Task should_stamp_batch_grant_with_the_current_tenant()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];

        _currentTenant.Id.Returns("tenant-a");
        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([new PermissionGrantRecord(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, false)]);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then — both the replaced denial and the new record carry the current tenant
        await _repository
            .Received(2)
            .InsertManyAsync(
                Arg.Is<IEnumerable<PermissionGrantRecord>>(static records =>
                    records.All(static r => r.TenantId == "tenant-a")
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_refuse_a_padded_current_tenant_before_touching_storage()
    {
        // given — SQL Server ignores trailing spaces, so a padded tenant id would share another tenant's rows
        _currentTenant.Id.Returns("acme ");

        // when
        var single = async () => await _sut.GrantAsync("Users.Create", _ProviderName, _ProviderKey, AbortToken);
        var batch = async () =>
            await _sut.GrantAsync(["Users.Create", "Users.Delete"], _ProviderName, _ProviderKey, AbortToken);

        // then
        await single.Should().ThrowExactlyAsync<ArgumentException>();
        await batch.Should().ThrowExactlyAsync<ArgumentException>();
        _repository.ReceivedCalls().Should().BeEmpty();
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    #endregion GrantAsync - Single

    #region GrantAsync - Batch

    [Fact]
    public async Task should_batch_grant_multiple_permissions()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([]);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository
            .Received(1)
            .InsertManyAsync(
                Arg.Is<IEnumerable<PermissionGrantRecord>>(static records => records.Take(3).Count() == 2),
                AbortToken
            );
    }

    [Fact]
    public async Task should_skip_batch_when_all_already_granted()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var existingRecords = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
            new(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, true),
        };

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns(existingRecords);

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _repository.DidNotReceive().InsertManyAsync(Arg.Any<IEnumerable<PermissionGrantRecord>>(), AbortToken);
    }

    [Fact]
    public async Task should_convert_denials_when_every_name_already_has_a_record()
    {
        // given — the record count matches the requested names, but one record is an explicit denial
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var existingRecords = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
            new(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, false),
        };

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns(existingRecords);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _repository
            .Received(1)
            .DeleteManyAsync(
                Arg.Is<IReadOnlyCollection<PermissionGrantRecord>>(static r =>
                    r.Count == 1 && r.All(static x => x.Name == "Users.Update")
                ),
                AbortToken
            );
        await _repository
            .Received(1)
            .InsertManyAsync(
                Arg.Is<IEnumerable<PermissionGrantRecord>>(static records =>
                    records.All(static r => r.Name == "Users.Update" && r.IsGranted)
                ),
                AbortToken
            );
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(static d =>
                    d.Count == 2 && d.Values.All(static v => v.IsGranted == true)
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_grant_mixed_batch_of_absent_denied_and_granted_names()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update", "Users.Delete"];
        var existingRecords = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, false),
            new(Guid.NewGuid(), "Users.Delete", _ProviderName, _ProviderKey, true),
        };

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns(existingRecords);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then — the denial is converted and the missing name is inserted
        await _repository
            .Received(1)
            .InsertManyAsync(
                Arg.Is<IEnumerable<PermissionGrantRecord>>(static records =>
                    records.All(static r => r.Name == "Users.Update" && r.IsGranted)
                ),
                AbortToken
            );
        await _repository
            .Received(1)
            .InsertManyAsync(
                Arg.Is<IEnumerable<PermissionGrantRecord>>(static records =>
                    records.All(static r => r.Name == "Users.Create" && r.IsGranted)
                ),
                AbortToken
            );
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(static d =>
                    d.Count == 3 && d.Values.All(static v => v.IsGranted == true)
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_update_cache_on_batch_grant()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([]);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.GrantAsync(permissionNames, _ProviderName, _ProviderKey, cancellationToken: AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(static d =>
                    d.Count == 2 && d.Values.All(static v => v.IsGranted == true)
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    #endregion GrantAsync - Batch

    #region RevokeAsync - Single

    [Fact]
    public async Task should_insert_denial_when_no_record_exists()
    {
        // given
        const string permissionName = "Users.Delete";
        var newId = Guid.NewGuid();
        const string tenantId = "tenant-1";

        _repository
            .FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken)
            .Returns((PermissionGrantRecord?)null);
        _guidGenerator.Create().Returns(newId);
        _currentTenant.Id.Returns(tenantId);

        // when
        await _sut.RevokeAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository
            .Received(1)
            .InsertAsync(
                Arg.Is<PermissionGrantRecord>(r =>
                    r.Id == newId && r.Name == permissionName && !r.IsGranted && r.TenantId == tenantId
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_update_granted_record_to_denied()
    {
        // given
        const string permissionName = "Users.Create";
        var existingId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var grantedRecord = new PermissionGrantRecord(
            existingId,
            permissionName,
            _ProviderName,
            _ProviderKey,
            true,
            "tenant-1"
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(grantedRecord);
        _guidGenerator.Create().Returns(newId);

        // when
        await _sut.RevokeAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository.Received(1).DeleteAsync(grantedRecord, AbortToken);
        await _repository.Received(1).InsertAsync(Arg.Is<PermissionGrantRecord>(static r => !r.IsGranted), AbortToken);
    }

    [Fact]
    public async Task should_skip_when_already_denied()
    {
        // given
        const string permissionName = "Users.Delete";
        var deniedRecord = new PermissionGrantRecord(
            Guid.NewGuid(),
            permissionName,
            _ProviderName,
            _ProviderKey,
            false
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(deniedRecord);

        // when
        await _sut.RevokeAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository.DidNotReceive().InsertAsync(Arg.Any<PermissionGrantRecord>(), AbortToken);
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<PermissionGrantRecord>(), AbortToken);
    }

    [Fact]
    public async Task should_update_cache_on_revoke()
    {
        // given
        const string permissionName = "Users.Create";
        var cacheKey = PermissionGrantCacheItem.CalculateCacheKey(permissionName, _ProviderName, _ProviderKey);
        var grantedRecord = new PermissionGrantRecord(
            Guid.NewGuid(),
            permissionName,
            _ProviderName,
            _ProviderKey,
            true
        );

        _repository.FindAsync(permissionName, _ProviderName, _ProviderKey, AbortToken).Returns(grantedRecord);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.RevokeAsync(permissionName, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAsync(
                cacheKey,
                Arg.Is<PermissionGrantCacheItem>(static c => c.IsGranted == false),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    #endregion RevokeAsync - Single

    #region RevokeAsync - Batch

    [Fact]
    public async Task should_batch_revoke_multiple_permissions()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        var existingRecords = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
            new(Guid.NewGuid(), "Users.Update", _ProviderName, _ProviderKey, true),
        };

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns(existingRecords);
        _guidGenerator.Create().Returns(Guid.NewGuid());

        // when
        await _sut.RevokeAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository
            .Received(1)
            .DeleteManyAsync(
                Arg.Is<IReadOnlyCollection<PermissionGrantRecord>>(static r =>
                    r.Count == 2 && r.All(static x => x.IsGranted)
                ),
                AbortToken
            );
        await _repository
            .Received(1)
            .InsertManyAsync(
                Arg.Is<IEnumerable<PermissionGrantRecord>>(static records => records.All(static r => !r.IsGranted)),
                AbortToken
            );
    }

    [Fact]
    public async Task should_update_cache_on_batch_revoke()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];

        _repository
            .GetListAsync(Arg.Any<IReadOnlyCollection<string>>(), _ProviderName, _ProviderKey, AbortToken)
            .Returns([]);
        _guidGenerator.Create().Returns(Guid.NewGuid());
        _currentTenant.Id.Returns("tenant-1");

        // when
        await _sut.RevokeAsync(permissionNames, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _cache
            .Received(1)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(static d =>
                    d.Count == 2 && d.Values.All(static v => v.IsGranted == false)
                ),
                Arg.Any<TimeSpan>(),
                AbortToken
            );
    }

    #endregion RevokeAsync - Batch

    #region GetAllGrantsAsync

    [Fact]
    public async Task should_return_all_grants_from_repository()
    {
        // given
        var records = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
            new(Guid.NewGuid(), "Users.Delete", _ProviderName, _ProviderKey, false),
        };

        _repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken).Returns(records);

        // when
        var result = await _sut.GetAllGrantsAsync(_ProviderName, _ProviderKey, AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(static g => g.Name == "Users.Create" && g.IsGranted);
        result.Should().Contain(static g => g.Name == "Users.Delete" && !g.IsGranted);
    }

    #endregion GetAllGrantsAsync

    #region Helpers

    private static PermissionDefinition _CreatePermission(string name, bool isEnabled = true)
    {
        var ctor = typeof(PermissionDefinition).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(string), typeof(string), typeof(bool)],
            null
        );

        return (PermissionDefinition)ctor!.Invoke([name, name, isEnabled]);
    }

    #endregion Helpers

    #region Surrounding white space

    /// <summary>
    /// SQL Server ignores trailing spaces when comparing keys and PostgreSQL does not, so a padded key part would
    /// address another key's row on one provider only; the store must refuse it before any storage call.
    /// </summary>
    public static readonly TheoryData<string> PaddedKeyCalls =
    [
        "is-granted-name",
        "is-granted-provider-name",
        "is-granted-provider-key",
        "is-granted-leading-provider-key",
        "is-granted-many-names",
        "is-granted-many-provider-key",
        "get-all-grants",
        "grant-provider-key",
        "grant-many-names",
        "revoke-provider-key",
        "revoke-many-provider-key",
        "refresh-provider-key",
    ];

    [Theory]
    [MemberData(nameof(PaddedKeyCalls))]
    public async Task should_refuse_key_with_surrounding_white_space_before_touching_storage(string call)
    {
        // when
        Func<Task> action = call switch
        {
            "is-granted-name" => async () =>
                await _sut.IsGrantedAsync("Users.Create ", _ProviderName, _ProviderKey, AbortToken),
            "is-granted-provider-name" => async () =>
                await _sut.IsGrantedAsync("Users.Create", "Role ", _ProviderKey, AbortToken),
            "is-granted-provider-key" => async () =>
                await _sut.IsGrantedAsync("Users.Create", _ProviderName, "admin ", AbortToken),
            "is-granted-leading-provider-key" => async () =>
                await _sut.IsGrantedAsync("Users.Create", _ProviderName, " admin", AbortToken),
            "is-granted-many-names" => async () =>
                await _sut.IsGrantedAsync(["Users.Create", "Users.Delete "], _ProviderName, _ProviderKey, AbortToken),
            "is-granted-many-provider-key" => async () =>
                await _sut.IsGrantedAsync(["Users.Create", "Users.Delete"], _ProviderName, "admin ", AbortToken),
            "get-all-grants" => async () => await _sut.GetAllGrantsAsync(_ProviderName, "admin ", AbortToken),
            "grant-provider-key" => async () =>
                await _sut.GrantAsync("Users.Create", _ProviderName, "admin ", AbortToken),
            "grant-many-names" => async () =>
                await _sut.GrantAsync(["Users.Create", " Users.Delete"], _ProviderName, _ProviderKey, AbortToken),
            "revoke-provider-key" => async () =>
                await _sut.RevokeAsync("Users.Create", _ProviderName, "admin ", AbortToken),
            "revoke-many-provider-key" => async () =>
                await _sut.RevokeAsync(["Users.Create", "Users.Delete"], _ProviderName, "admin ", AbortToken),
            "refresh-provider-key" => async () => await _sut.RefreshAsync(_ProviderName, "admin ", AbortToken),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, null),
        };

        // then
        await action.Should().ThrowExactlyAsync<ArgumentException>().WithMessage("*starts or ends with white space*");
        _repository.ReceivedCalls().Should().BeEmpty();
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_key_with_a_nul_or_an_unpaired_surrogate_before_touching_storage()
    {
        // PostgreSQL fails the statement on NUL, and SqlClient sends an unpaired surrogate as U+FFFD, so SQL Server
        // would address one row for keys that differ only in which lone surrogate they carry.
        Func<Task>[] calls =
        [
            async () => await _sut.IsGrantedAsync("Users.Create", _ProviderName, "ad\0min", AbortToken),
            async () =>
                await _sut.IsGrantedAsync("Users.Create" + (char)0xD800, _ProviderName, _ProviderKey, AbortToken),
        ];

        foreach (var call in calls)
        {
            await call.Should()
                .ThrowExactlyAsync<ArgumentException>()
                .WithMessage("*storage providers would disagree*");
        }

        _repository.ReceivedCalls().Should().BeEmpty();
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    #endregion Surrounding white space
}
