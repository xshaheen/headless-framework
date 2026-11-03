// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Grants;

/// <summary>
/// A reader that misses the cache loads the rows, then fills the cache. A write that lands between the load and the
/// fill must win: the fill must not put the pre-write status back for the whole cache expiration. Each test runs the
/// write from inside the reader's repository load, the exact interleaving, on a real in-memory cache. Two writers race
/// the same way: the one that commits last must win, even when the other one reaches the cache after it.
/// </summary>
public sealed class PermissionGrantStoreCacheRaceTests : TestBase
{
    private const string _ProviderName = "Role";
    private const string _ProviderKey = "admin";
    private const string _Permission = "Users.Create";
    private const string _OtherPermission = "Users.Delete";

    private readonly InMemoryCache _rootCache = new(TimeProvider.System, new InMemoryCacheOptions());
    private readonly RacingGrantRepository _repository = new();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();
    private readonly List<Func<ValueTask>> _afterCommit = [];
    private readonly PermissionGrantStore _sut;

    public PermissionGrantStoreCacheRaceTests()
    {
        var definitions = Substitute.For<IPermissionDefinitionManager>();
        definitions
            .GetPermissionsAsync(Arg.Any<CancellationToken>())
            .Returns([_CreatePermission(_Permission), _CreatePermission(_OtherPermission)]);

        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        _unit.OnCompleted(Arg.Do<Func<ValueTask>>(_afterCommit.Add)).Returns(Substitute.For<IDisposable>());

        _sut = new PermissionGrantStore(
            definitions,
            _repository,
            guidGenerator,
            new Cache<PermissionGrantCacheItem>(_rootCache),
            Substitute.For<ICurrentTenant>(),
            Options.Create(new PermissionManagementOptions()),
            NullLogger<PermissionGrantStore>.Instance
        );
    }

    [Fact]
    public async Task should_keep_a_revoke_when_a_cache_miss_loaded_the_grant_before_it()
    {
        // given a stored grant and a cold cache
        _repository.Rows.Add(_Grant(_Permission, isGranted: true));
        _repository.DuringNextLoad = () => _sut.RevokeAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // when a check misses, loads the grant, and a revoke completes before the check fills the cache
        var raced = await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // then the next check sees the revoke, not the grant the racing load read
        raced.Should().Be(PermissionGrantStatus.Granted);
        (await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_keep_a_revoke_of_another_permission_when_a_cache_miss_reloads_the_role()
    {
        // given grants for two permissions and a cold cache
        _repository.Rows.Add(_Grant(_Permission, isGranted: true));
        _repository.Rows.Add(_Grant(_OtherPermission, isGranted: true));
        _repository.DuringNextLoad = () => _sut.RevokeAsync(_OtherPermission, _ProviderName, _ProviderKey, AbortToken);

        // when a check of one permission reloads the whole role while the other permission is revoked
        await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // then
        (await _sut.IsGrantedAsync(_OtherPermission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_keep_a_batched_revoke_when_a_batched_check_loaded_the_grants_before_it()
    {
        // given
        _repository.Rows.Add(_Grant(_Permission, isGranted: true));
        _repository.Rows.Add(_Grant(_OtherPermission, isGranted: true));
        _repository.DuringNextLoad = () =>
            _sut.RevokeAsync([_Permission, _OtherPermission], _ProviderName, _ProviderKey, AbortToken);

        // when a batched check misses both names and the revoke lands inside its load
        await _sut.IsGrantedAsync([_Permission, _OtherPermission], _ProviderName, _ProviderKey, AbortToken);

        // then
        var statuses = await _sut.IsGrantedAsync(
            [_Permission, _OtherPermission],
            _ProviderName,
            _ProviderKey,
            AbortToken
        );
        statuses.Values.Should().AllSatisfy(x => x.Should().Be(PermissionGrantStatus.Prohibited));
    }

    [Fact]
    public async Task should_keep_a_revoke_when_a_multi_key_check_loaded_the_grant_before_it()
    {
        // given
        _repository.Rows.Add(_Grant(_Permission, isGranted: true));
        _repository.DuringNextLoad = () => _sut.RevokeAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // when a check across provider keys misses and the revoke lands inside its load
        await _sut.IsGrantedAsync([_Permission], _ProviderName, [_ProviderKey, "editor"], AbortToken);

        // then
        (await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_keep_an_enlisted_revoke_when_a_cache_miss_loaded_the_grant_before_its_commit()
    {
        // given
        _repository.Rows.Add(_Grant(_Permission, isGranted: true));
        _repository.DuringNextLoad = async () =>
        {
            await _sut.RevokeEnlistedAsync(_unit, [_Permission], _ProviderName, _ProviderKey, AbortToken);
            await _CommitAsync();
        };

        // when a check misses, loads the grant, and an enlisted revoke commits before the check fills the cache
        await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // then
        (await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_keep_a_delete_when_a_cache_miss_loaded_the_grant_before_it()
    {
        // given
        _repository.Rows.Add(_Grant(_Permission, isGranted: true));
        _repository.DuringNextLoad = async () => _ = await _sut.DeleteAsync(_ProviderName, _ProviderKey, AbortToken);

        // when
        await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // then
        (await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_keep_an_enlisted_delete_when_a_cache_miss_loaded_the_grant_before_its_commit()
    {
        // given
        _repository.Rows.Add(_Grant(_Permission, isGranted: true));
        _repository.DuringNextLoad = async () =>
        {
            await _sut.DeleteEnlistedAsync(_unit, _ProviderName, _ProviderKey, AbortToken);
            await _CommitAsync();
        };

        // when
        await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // then
        (await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_cache_the_last_committed_status_when_the_first_writer_updates_the_cache_last()
    {
        // given a grant whose insert lets a revoke commit and update the cache before the grant returns
        _repository.DuringNextWrite = () => _sut.RevokeAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // when the grant then updates the cache
        await _sut.GrantAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);

        // then the cache holds what the repository kept, the revoke's denial
        _repository.Rows.Should().ContainSingle().Which.IsGranted.Should().BeFalse();
        (await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_replace_cached_statuses_on_an_explicit_refresh()
    {
        // given a cached grant and a revoke written straight to storage, bypassing the store
        _repository.Rows.Add(_Grant(_Permission, isGranted: true));
        await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken);
        _repository.Rows.Clear();
        _repository.Rows.Add(_Grant(_Permission, isGranted: false));

        // when
        await _sut.RefreshAsync(_ProviderName, _ProviderKey, AbortToken);

        // then
        (await _sut.IsGrantedAsync(_Permission, _ProviderName, _ProviderKey, AbortToken))
            .Should()
            .Be(PermissionGrantStatus.Prohibited);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _rootCache.Dispose();
        await base.DisposeAsyncCore();
    }

    private async Task _CommitAsync()
    {
        foreach (var callback in _afterCommit)
        {
            await callback();
        }

        _afterCommit.Clear();
    }

    private static PermissionGrantRecord _Grant(string name, bool isGranted)
    {
        return new PermissionGrantRecord(Guid.NewGuid(), name, _ProviderName, _ProviderKey, isGranted);
    }

    private static PermissionDefinition _CreatePermission(string name)
    {
        var ctor = typeof(PermissionDefinition).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(string), typeof(string), typeof(bool)],
            null
        );

        return (PermissionDefinition)ctor!.Invoke([name, name, true]);
    }

    /// <summary>
    /// One tenant's rows in memory. A read that loads rows for a cache fill returns what it read before running
    /// <see cref="DuringNextLoad" />, the write racing it. A single insert lands its row, then runs
    /// <see cref="DuringNextWrite" />, a second writer racing it, before it returns.
    /// </summary>
    private sealed class RacingGrantRepository : IPermissionGrantRepository
    {
        public List<PermissionGrantRecord> Rows { get; } = [];

        public Func<Task>? DuringNextLoad { get; set; }

        public Func<Task>? DuringNextWrite { get; set; }

        public Task<PermissionGrantRecord?> FindAsync(
            string name,
            string providerName,
            string providerKey,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                Rows.Find(x =>
                    _Matches(x, providerName, providerKey) && string.Equals(x.Name, name, StringComparison.Ordinal)
                )
            );
        }

        public Task<List<PermissionGrantRecord>> GetListAsync(
            string providerName,
            string providerKey,
            CancellationToken cancellationToken = default
        )
        {
            return _LoadAsync(x => _Matches(x, providerName, providerKey));
        }

        public Task<List<PermissionGrantRecord>> GetListAsync(
            IReadOnlyCollection<string> names,
            string providerName,
            string providerKey,
            CancellationToken cancellationToken = default
        )
        {
            return _LoadAsync(x => _Matches(x, providerName, providerKey) && names.Contains(x.Name));
        }

        public Task<List<PermissionGrantRecord>> GetListAsync(
            IReadOnlyCollection<string> names,
            string providerName,
            IReadOnlyCollection<string> providerKeys,
            CancellationToken cancellationToken = default
        )
        {
            return _LoadAsync(x =>
                string.Equals(x.ProviderName, providerName, StringComparison.Ordinal)
                && providerKeys.Contains(x.ProviderKey)
                && names.Contains(x.Name)
            );
        }

        public async Task InsertAsync(
            PermissionGrantRecord permissionGrant,
            CancellationToken cancellationToken = default
        )
        {
            Rows.Add(permissionGrant);

            var racingWrite = DuringNextWrite;
            DuringNextWrite = null;

            if (racingWrite is not null)
            {
                await racingWrite();
            }
        }

        public Task InsertManyAsync(
            IEnumerable<PermissionGrantRecord> permissionGrants,
            CancellationToken cancellationToken = default
        )
        {
            Rows.AddRange(permissionGrants);

            return Task.CompletedTask;
        }

        public Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
        {
            Rows.RemoveAll(x => x.Id == permissionGrant.Id);

            return Task.CompletedTask;
        }

        public Task DeleteManyAsync(
            IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
            CancellationToken cancellationToken = default
        )
        {
            Rows.RemoveAll(x => permissionGrants.Any(g => g.Id == x.Id));

            return Task.CompletedTask;
        }

        // The writer's own reads inside the race are not cache fills, so they read the rows directly.
        public Task<List<PermissionGrantRecord>> GetListEnlistedAsync(
            IUnitOfWork unitOfWork,
            string providerName,
            string providerKey,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(Rows.FindAll(x => _Matches(x, providerName, providerKey)));
        }

        public Task<List<PermissionGrantRecord>> GetListEnlistedAsync(
            IUnitOfWork unitOfWork,
            IReadOnlyCollection<string> names,
            string providerName,
            string providerKey,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(Rows.FindAll(x => _Matches(x, providerName, providerKey) && names.Contains(x.Name)));
        }

        public Task InsertManyEnlistedAsync(
            IUnitOfWork unitOfWork,
            IEnumerable<PermissionGrantRecord> permissionGrants,
            CancellationToken cancellationToken
        )
        {
            return InsertManyAsync(permissionGrants, cancellationToken);
        }

        public Task DeleteManyEnlistedAsync(
            IUnitOfWork unitOfWork,
            IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
            CancellationToken cancellationToken
        )
        {
            return DeleteManyAsync(permissionGrants, cancellationToken);
        }

        private async Task<List<PermissionGrantRecord>> _LoadAsync(Predicate<PermissionGrantRecord> match)
        {
            var loaded = Rows.FindAll(match);
            var racingWrite = DuringNextLoad;
            DuringNextLoad = null;

            if (racingWrite is not null)
            {
                await racingWrite();
            }

            return loaded;
        }

        private static bool _Matches(PermissionGrantRecord record, string providerName, string providerKey)
        {
            return string.Equals(record.ProviderName, providerName, StringComparison.Ordinal)
                && string.Equals(record.ProviderKey, providerKey, StringComparison.Ordinal);
        }
    }
}
