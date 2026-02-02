using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Cache;
using Headless.Testing.Tests;

namespace Tests;

public sealed class InMemoryResourceLockStorageTests : TestBase
{
    private readonly InMemoryCache _cache = new(TimeProvider.System, new InMemoryCacheOptions());

    private IResourceLockStorage CreateStorage() => new CacheResourceLockStorage(_cache);

    protected override ValueTask DisposeAsyncCore()
    {
        _cache.Dispose();
        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_insert_lock()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        // when
        var result = await storage.InsertAsync(key, lockId, ttl);

        // then
        result.Should().BeTrue();
        var storedId = await storage.GetAsync(key);
        storedId.Should().Be(lockId);
    }

    [Fact]
    public async Task should_not_insert_when_exists()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId1 = Faker.Random.Guid().ToString("N");
        var lockId2 = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key, lockId1, ttl);

        // when
        var result = await storage.InsertAsync(key, lockId2, ttl);

        // then
        result.Should().BeFalse();
        var storedId = await storage.GetAsync(key);
        storedId.Should().Be(lockId1);
    }

    [Fact]
    public async Task should_remove_lock()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key, lockId, ttl);

        // when
        var result = await storage.RemoveIfEqualAsync(key, lockId);

        // then
        result.Should().BeTrue();
        var exists = await storage.ExistsAsync(key);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_when_different_id()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var differentId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key, lockId, ttl);

        // when
        var result = await storage.RemoveIfEqualAsync(key, differentId);

        // then
        result.Should().BeFalse();
        var storedId = await storage.GetAsync(key);
        storedId.Should().Be(lockId);
    }

    [Fact]
    public async Task should_expire_after_ttl()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMilliseconds(100);

        await storage.InsertAsync(key, lockId, ttl);

        // when
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // then
        var exists = await storage.ExistsAsync(key);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_get_lock_id()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key, lockId, ttl);

        // when
        var result = await storage.GetAsync(key);

        // then
        result.Should().Be(lockId);
    }

    [Fact]
    public async Task should_return_null_when_not_exists()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);

        // when
        var result = await storage.GetAsync(key);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_check_exists()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        // when - not exists
        var existsBefore = await storage.ExistsAsync(key);

        await storage.InsertAsync(key, lockId, ttl);

        // when - exists
        var existsAfter = await storage.ExistsAsync(key);

        // then
        existsBefore.Should().BeFalse();
        existsAfter.Should().BeTrue();
    }

    [Fact]
    public async Task should_get_expiration()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key, lockId, ttl);

        // when
        var expiration = await storage.GetExpirationAsync(key);

        // then
        expiration.Should().NotBeNull();
        expiration!.Value.Should().BeCloseTo(ttl, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_return_null_expiration_when_not_exists()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);

        // when
        var expiration = await storage.GetExpirationAsync(key);

        // then
        expiration.Should().BeNull();
    }

    [Fact]
    public async Task should_replace_if_equal()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var newLockId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key, lockId, ttl);

        // when
        var result = await storage.ReplaceIfEqualAsync(key, lockId, newLockId, ttl);

        // then
        result.Should().BeTrue();
        var storedId = await storage.GetAsync(key);
        storedId.Should().Be(newLockId);
    }

    [Fact]
    public async Task should_not_replace_if_not_equal()
    {
        // given
        var storage = CreateStorage();
        var key = Faker.Random.String2(5, 10);
        var lockId = Faker.Random.Guid().ToString("N");
        var differentId = Faker.Random.Guid().ToString("N");
        var newLockId = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key, lockId, ttl);

        // when
        var result = await storage.ReplaceIfEqualAsync(key, differentId, newLockId, ttl);

        // then
        result.Should().BeFalse();
        var storedId = await storage.GetAsync(key);
        storedId.Should().Be(lockId);
    }

    [Fact]
    public async Task should_get_all_by_prefix()
    {
        // given
        var storage = CreateStorage();
        var prefix = $"test:{Faker.Random.String2(5, 10)}:";
        var key1 = $"{prefix}key1";
        var key2 = $"{prefix}key2";
        var lockId1 = Faker.Random.Guid().ToString("N");
        var lockId2 = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key1, lockId1, ttl);
        await storage.InsertAsync(key2, lockId2, ttl);

        // when
        var result = await storage.GetAllByPrefixAsync(prefix);

        // then
        result.Should().HaveCount(2);
        result.Should().ContainKey(key1).WhoseValue.Should().Be(lockId1);
        result.Should().ContainKey(key2).WhoseValue.Should().Be(lockId2);
    }

    [Fact]
    public async Task should_get_count()
    {
        // given
        var storage = CreateStorage();
        var prefix = $"count-test:{Faker.Random.String2(5, 10)}:";
        var key1 = $"{prefix}key1";
        var key2 = $"{prefix}key2";
        var lockId1 = Faker.Random.Guid().ToString("N");
        var lockId2 = Faker.Random.Guid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        await storage.InsertAsync(key1, lockId1, ttl);
        await storage.InsertAsync(key2, lockId2, ttl);

        // when
        var count = await storage.GetCountAsync(prefix);

        // then
        count.Should().Be(2);
    }
}
