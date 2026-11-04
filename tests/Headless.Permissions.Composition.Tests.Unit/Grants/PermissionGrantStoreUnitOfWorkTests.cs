// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests.Grants;

public sealed class PermissionGrantStoreUnitOfWorkTests : TestBase
{
    private const string _ProviderName = "Role";
    private const string _ProviderKey = "admin";

    private readonly IPermissionGrantRepository _repository = Substitute.For<IPermissionGrantRepository>();
    private readonly ICache<PermissionGrantCacheItem> _cache = Substitute.For<ICache<PermissionGrantCacheItem>>();
    private readonly ICurrentTenant _currentTenant = Substitute.For<ICurrentTenant>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();
    private readonly List<Func<ValueTask>> _afterCommit = [];
    private readonly PermissionGrantStore _sut;

    public PermissionGrantStoreUnitOfWorkTests()
    {
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());
        _unit.OnCompleted(Arg.Do<Func<ValueTask>>(_afterCommit.Add)).Returns(Substitute.For<IDisposable>());

        // After the commit the store reads the written names back and inserts them into an empty cache; a test that
        // cares what the repository holds then overrides the read-back.
        _repository
            .GetListAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([]);
        _cache
            .GetAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                ValueTask.FromResult<IDictionary<string, CacheValue<PermissionGrantCacheItem>>>(
                    new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal)
                )
            );
        _cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<PermissionGrantCacheItem?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));

        _sut = new PermissionGrantStore(
            Substitute.For<IPermissionDefinitionManager>(),
            _repository,
            guidGenerator,
            _cache,
            _currentTenant,
            Options.Create(new PermissionManagementOptions()),
            Substitute.For<ILogger<PermissionGrantStore>>()
        );
    }

    [Fact]
    public async Task should_grant_through_the_enlisted_repository_and_clear_the_cache_only_after_commit()
    {
        // given a denied record and a name with no record
        var denied = new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, false);
        _repository
            .GetListEnlistedAsync(
                _unit,
                Arg.Any<IReadOnlyCollection<string>>(),
                _ProviderName,
                _ProviderKey,
                AbortToken
            )
            .Returns([denied]);

        _currentTenant.Id.Returns("t1");

        // when
        await _sut.GrantEnlistedAsync(_unit, ["Users.Create", "Users.Delete"], _ProviderName, _ProviderKey, AbortToken);

        // then the denial is replaced and the new grant inserted, all inside the unit
        await _repository
            .Received(1)
            .DeleteManyEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyCollection<PermissionGrantRecord>>(x => x.Single() == denied),
                AbortToken
            );
        await _repository
            .Received(1)
            .InsertManyEnlistedAsync(
                _unit,
                Arg.Is<IEnumerable<PermissionGrantRecord>>(x =>
                    x.Single().Name == "Users.Create" && x.Single().IsGranted && x.Single().TenantId == "t1"
                ),
                AbortToken
            );
        await _repository
            .Received(1)
            .InsertManyEnlistedAsync(
                _unit,
                Arg.Is<IEnumerable<PermissionGrantRecord>>(x =>
                    x.Single().Name == "Users.Delete" && x.Single().IsGranted && x.Single().TenantId == "t1"
                ),
                AbortToken
            );
        _repository
            .ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Should()
            .OnlyContain(name => name.EndsWith("EnlistedAsync", StringComparison.Ordinal));
        _cache.ReceivedCalls().Should().BeEmpty();

        // and the committed status is read back and written once the unit commits, so a reader that loaded before
        // the commit cannot fill the entry with what it read
        _repository
            .GetListAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                _ProviderName,
                _ProviderKey,
                Arg.Any<CancellationToken>()
            )
            .Returns([
                new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
                new PermissionGrantRecord(Guid.NewGuid(), "Users.Delete", _ProviderName, _ProviderKey, true),
            ]);
        await _CommitAsync();
        _InsertedStatuses()
            .Should()
            .Equal(
                new Dictionary<string, bool?>(StringComparer.Ordinal)
                {
                    [_CacheKey("Users.Create")] = true,
                    [_CacheKey("Users.Delete")] = true,
                }
            );
    }

    [Fact]
    public async Task should_revoke_inside_the_unit_keeping_the_replaced_grants_tenant()
    {
        // given
        var granted = new PermissionGrantRecord(
            Guid.NewGuid(),
            "Users.Create",
            _ProviderName,
            _ProviderKey,
            true,
            tenantId: "owner"
        );
        _repository
            .GetListEnlistedAsync(
                _unit,
                Arg.Any<IReadOnlyCollection<string>>(),
                _ProviderName,
                _ProviderKey,
                AbortToken
            )
            .Returns([granted]);

        // when
        await _sut.RevokeEnlistedAsync(_unit, ["Users.Create"], _ProviderName, _ProviderKey, AbortToken);

        // then
        await _repository
            .Received(1)
            .InsertManyEnlistedAsync(
                _unit,
                Arg.Is<IEnumerable<PermissionGrantRecord>>(x =>
                    !x.Single().IsGranted && x.Single().TenantId == "owner"
                ),
                AbortToken
            );
        _afterCommit.Should().ContainSingle();
    }

    [Fact]
    public async Task should_clear_the_cache_under_the_tenant_that_wrote()
    {
        // given
        _currentTenant.Id.Returns("tenant-a");
        _currentTenant.Name.Returns("Tenant A");
        _repository
            .GetListEnlistedAsync(
                _unit,
                Arg.Any<IReadOnlyCollection<string>>(),
                _ProviderName,
                _ProviderKey,
                AbortToken
            )
            .Returns([]);

        // when the unit completes after the code left the tenant
        await _sut.GrantEnlistedAsync(_unit, ["Users.Create"], _ProviderName, _ProviderKey, AbortToken);
        _currentTenant.Id.Returns((string?)null);
        await _CommitAsync();

        // then
        _currentTenant.Received(1).Change("tenant-a", "Tenant A");
    }

    [Fact]
    public async Task should_log_rather_than_throw_when_the_cache_fails_after_commit()
    {
        // given
        _repository
            .GetListEnlistedAsync(
                _unit,
                Arg.Any<IReadOnlyCollection<string>>(),
                _ProviderName,
                _ProviderKey,
                AbortToken
            )
            .Returns([]);
        _cache
            .GetAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache down"));
        await _sut.GrantEnlistedAsync(_unit, ["Users.Create"], _ProviderName, _ProviderKey, AbortToken);

        // when
        var act = _CommitAsync;

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_delete_inside_the_unit_and_return_the_removed_names()
    {
        // given
        List<PermissionGrantRecord> grants =
        [
            new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true),
            new(Guid.NewGuid(), "Users.Delete", _ProviderName, _ProviderKey, false),
        ];
        _repository.GetListEnlistedAsync(_unit, _ProviderName, _ProviderKey, AbortToken).Returns(grants);

        // when
        var removed = await _sut.DeleteEnlistedAsync(_unit, _ProviderName, _ProviderKey, AbortToken);

        // then
        removed.Should().BeEquivalentTo("Users.Create", "Users.Delete");
        await _repository.Received(1).DeleteManyEnlistedAsync(_unit, grants, AbortToken);
        _cache.ReceivedCalls().Should().BeEmpty();
        await _CommitAsync();
        _InsertedStatuses()
            .Should()
            .Equal(
                new Dictionary<string, bool?>(StringComparer.Ordinal)
                {
                    [_CacheKey("Users.Create")] = null,
                    [_CacheKey("Users.Delete")] = null,
                }
            );
    }

    [Fact]
    public async Task should_register_nothing_when_an_enlisted_delete_finds_no_grants()
    {
        // given
        _repository.GetListEnlistedAsync(_unit, _ProviderName, _ProviderKey, AbortToken).Returns([]);

        // when
        var removed = await _sut.DeleteEnlistedAsync(_unit, _ProviderName, _ProviderKey, AbortToken);

        // then
        removed.Should().BeEmpty();
        _afterCommit.Should().BeEmpty();
        await _repository.DidNotReceiveWithAnyArgs().DeleteManyEnlistedAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_mark_the_cache_entries_of_an_autonomous_delete_undefined()
    {
        // given
        List<PermissionGrantRecord> grants = [new(Guid.NewGuid(), "Users.Create", _ProviderName, _ProviderKey, true)];
        _repository.GetListAsync(_ProviderName, _ProviderKey, AbortToken).Returns(grants);

        // when
        var removed = await _sut.DeleteAsync(_ProviderName, _ProviderKey, AbortToken);

        // then
        removed.Should().Equal("Users.Create");
        await _repository.Received(1).DeleteManyAsync(grants, AbortToken);
        _InsertedStatuses()
            .Should()
            .Equal(new Dictionary<string, bool?>(StringComparer.Ordinal) { [_CacheKey("Users.Create")] = null });
    }

    [Fact]
    public async Task should_refuse_a_padded_key_before_enlisting()
    {
        // when
        var act = () => _sut.GrantEnlistedAsync(_unit, ["Users.Create"], _ProviderName, "admin ", AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<ArgumentException>();
        _repository.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_enlisted_writes_through_a_repository_that_cannot_join_a_unit()
    {
        // given a repository that implements only the autonomous members
        IPermissionGrantRepository repository = Substitute.ForPartsOf<AutonomousOnlyRepository>();

        // when
        var act = () => repository.GetListEnlistedAsync(_unit, _ProviderName, _ProviderKey, AbortToken);

        // then
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*cannot run inside a unit of work*");
    }

    /// <summary>The status each cache key was inserted with, read from the calls the cache received.</summary>
    private Dictionary<string, bool?> _InsertedStatuses()
    {
        return _cache
            .ReceivedCalls()
            .Where(call =>
                string.Equals(
                    call.GetMethodInfo().Name,
                    nameof(ICache<PermissionGrantCacheItem>.TryInsertAsync),
                    StringComparison.Ordinal
                )
            )
            .ToDictionary(
                call => (string)call.GetArguments()[0]!,
                call => ((PermissionGrantCacheItem)call.GetArguments()[1]!).IsGranted,
                StringComparer.Ordinal
            );
    }

    private static string _CacheKey(string name)
    {
        return PermissionGrantCacheItem.CalculateCacheKey(name, _ProviderName, _ProviderKey);
    }

    private async Task _CommitAsync()
    {
        foreach (var work in _afterCommit)
        {
            await work();
        }
    }

    public abstract class AutonomousOnlyRepository : IPermissionGrantRepository
    {
        public abstract Task<PermissionGrantRecord?> FindAsync(
            string name,
            string providerName,
            string providerKey,
            CancellationToken cancellationToken = default
        );

        public abstract Task<List<PermissionGrantRecord>> GetListAsync(
            string providerName,
            string providerKey,
            CancellationToken cancellationToken = default
        );

        public abstract Task<List<PermissionGrantRecord>> GetListAsync(
            IReadOnlyCollection<string> names,
            string providerName,
            string providerKey,
            CancellationToken cancellationToken = default
        );

        public abstract Task<List<PermissionGrantRecord>> GetListAsync(
            IReadOnlyCollection<string> names,
            string providerName,
            IReadOnlyCollection<string> providerKeys,
            CancellationToken cancellationToken = default
        );

        public abstract Task InsertAsync(
            PermissionGrantRecord permissionGrant,
            CancellationToken cancellationToken = default
        );

        public abstract Task InsertManyAsync(
            IEnumerable<PermissionGrantRecord> permissionGrants,
            CancellationToken cancellationToken = default
        );

        public abstract Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken);

        public abstract Task DeleteManyAsync(
            IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
            CancellationToken cancellationToken = default
        );

        public abstract Task SaveAsync(
            IReadOnlyCollection<PermissionGrantRecord> inserted,
            IReadOnlyCollection<PermissionGrantRecord> deleted,
            CancellationToken cancellationToken = default
        );
    }
}
