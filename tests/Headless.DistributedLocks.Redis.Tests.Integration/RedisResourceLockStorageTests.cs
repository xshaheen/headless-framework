// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Integration tests for <see cref="Headless.DistributedLocks.Redis.RedisResourceLockStorage"/>.
/// Tests verify Redis-specific behaviors: SET NX, PSETEX expiry, and Lua script atomicity.
/// </summary>
[Collection<RedisTestFixture>]
public sealed class RedisResourceLockStorageTests(RedisTestFixture fixture) : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await fixture.ConnectionMultiplexer.FlushAllAsync();
    }

    #region InsertAsync (SET NX behavior)

    [Fact]
    public async Task should_insert_lock_when_key_not_exists()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");

        // when
        var result = await fixture.LockStorage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));

        // then
        result.Should().BeTrue();
        var stored = await fixture.LockStorage.GetAsync(key);
        stored.Should().Be(lockId);
    }

    [Fact]
    public async Task should_not_insert_when_key_already_exists()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var originalLockId = Guid.NewGuid().ToString("N");
        var newLockId = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key, originalLockId, TimeSpan.FromMinutes(5));

        // when
        var result = await fixture.LockStorage.InsertAsync(key, newLockId, TimeSpan.FromMinutes(5));

        // then
        result.Should().BeFalse();
        var stored = await fixture.LockStorage.GetAsync(key);
        stored.Should().Be(originalLockId);
    }

    [Fact]
    public async Task should_insert_with_nx_atomically_under_concurrent_access()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var successCount = 0;
        var lockIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid().ToString("N")).ToList();

        // when
        await Parallel.ForEachAsync(
            lockIds,
            new ParallelOptions { MaxDegreeOfParallelism = 50 },
            async (lockId, _) =>
            {
                var result = await fixture.LockStorage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));
                if (result)
                {
                    Interlocked.Increment(ref successCount);
                }
            }
        );

        // then - only one should succeed
        successCount.Should().Be(1);
        var stored = await fixture.LockStorage.GetAsync(key);
        stored.Should().NotBeNullOrEmpty();
        lockIds.Should().Contain(stored!);
    }

    #endregion

    #region Expiration (PSETEX behavior)

    [Fact]
    public async Task should_set_expiration_on_insert()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");
        var ttl = TimeSpan.FromMinutes(5);

        // when
        await fixture.LockStorage.InsertAsync(key, lockId, ttl);

        // then
        var expiration = await fixture.LockStorage.GetExpirationAsync(key);
        expiration.Should().NotBeNull();
        expiration!.Value.TotalMinutes.Should().BeGreaterThan(4);
        expiration.Value.TotalMinutes.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task should_expire_lock_after_ttl()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");
        var ttl = TimeSpan.FromMilliseconds(100);
        await fixture.LockStorage.InsertAsync(key, lockId, ttl);

        // when
        await Task.Delay(200);

        // then
        var exists = await fixture.LockStorage.ExistsAsync(key);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_allow_reacquire_after_expiration()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId1 = Guid.NewGuid().ToString("N");
        var lockId2 = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key, lockId1, TimeSpan.FromMilliseconds(100));

        // when - wait for expiration
        await Task.Delay(200);
        var result = await fixture.LockStorage.InsertAsync(key, lockId2, TimeSpan.FromMinutes(5));

        // then
        result.Should().BeTrue();
        var stored = await fixture.LockStorage.GetAsync(key);
        stored.Should().Be(lockId2);
    }

    [Fact]
    public async Task should_insert_without_expiration_when_ttl_is_null()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");

        // when
        await fixture.LockStorage.InsertAsync(key, lockId, ttl: null);

        // then
        var expiration = await fixture.LockStorage.GetExpirationAsync(key);
        expiration.Should().BeNull();
        var exists = await fixture.LockStorage.ExistsAsync(key);
        exists.Should().BeTrue();
    }

    #endregion

    #region RemoveIfEqualAsync (Lua script atomic compare-and-delete)

    [Fact]
    public async Task should_remove_when_lock_id_matches()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));

        // when
        var result = await fixture.LockStorage.RemoveIfEqualAsync(key, lockId);

        // then
        result.Should().BeTrue();
        var exists = await fixture.LockStorage.ExistsAsync(key);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_remove_when_lock_id_does_not_match()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");
        var wrongLockId = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));

        // when
        var result = await fixture.LockStorage.RemoveIfEqualAsync(key, wrongLockId);

        // then
        result.Should().BeFalse();
        var exists = await fixture.LockStorage.ExistsAsync(key);
        exists.Should().BeTrue();
        var stored = await fixture.LockStorage.GetAsync(key);
        stored.Should().Be(lockId);
    }

    [Fact]
    public async Task should_not_remove_when_key_does_not_exist()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");

        // when
        var result = await fixture.LockStorage.RemoveIfEqualAsync(key, lockId);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_atomically_with_lua_script()
    {
        // given - simulate concurrent removal attempts
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var lockId = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key, lockId, TimeSpan.FromMinutes(5));

        var removeCount = 0;

        // when - multiple concurrent remove attempts
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 20),
            new ParallelOptions { MaxDegreeOfParallelism = 20 },
            async (_, _) =>
            {
                var result = await fixture.LockStorage.RemoveIfEqualAsync(key, lockId);
                if (result)
                {
                    Interlocked.Increment(ref removeCount);
                }
            }
        );

        // then - only one should succeed due to atomic Lua script
        removeCount.Should().Be(1);
    }

    #endregion

    #region ReplaceIfEqualAsync (Lua script atomic compare-and-swap)

    [Fact]
    public async Task should_replace_when_expected_id_matches()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var originalId = Guid.NewGuid().ToString("N");
        var newId = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key, originalId, TimeSpan.FromMinutes(5));

        // when
        var result = await fixture.LockStorage.ReplaceIfEqualAsync(key, originalId, newId, TimeSpan.FromMinutes(10));

        // then
        result.Should().BeTrue();
        var stored = await fixture.LockStorage.GetAsync(key);
        stored.Should().Be(newId);
    }

    [Fact]
    public async Task should_not_replace_when_expected_id_does_not_match()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var originalId = Guid.NewGuid().ToString("N");
        var wrongExpectedId = Guid.NewGuid().ToString("N");
        var newId = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key, originalId, TimeSpan.FromMinutes(5));

        // when
        var result = await fixture.LockStorage.ReplaceIfEqualAsync(key, wrongExpectedId, newId, TimeSpan.FromMinutes(10));

        // then
        result.Should().BeFalse();
        var stored = await fixture.LockStorage.GetAsync(key);
        stored.Should().Be(originalId);
    }

    [Fact]
    public async Task should_update_expiration_on_replace()
    {
        // given
        var key = $"lock:{Faker.Random.AlphaNumeric(10)}";
        var originalId = Guid.NewGuid().ToString("N");
        var newId = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key, originalId, TimeSpan.FromMinutes(1));

        // when
        await fixture.LockStorage.ReplaceIfEqualAsync(key, originalId, newId, TimeSpan.FromMinutes(30));

        // then
        var expiration = await fixture.LockStorage.GetExpirationAsync(key);
        expiration.Should().NotBeNull();
        expiration!.Value.TotalMinutes.Should().BeGreaterThan(25);
    }

    #endregion

    #region GetAllByPrefixAsync and GetCountAsync

    [Fact]
    public async Task should_get_all_locks_by_prefix()
    {
        // given
        var prefix = $"test-prefix-{Faker.Random.AlphaNumeric(5)}:";
        var key1 = $"{prefix}resource1";
        var key2 = $"{prefix}resource2";
        var lockId1 = Guid.NewGuid().ToString("N");
        var lockId2 = Guid.NewGuid().ToString("N");
        await fixture.LockStorage.InsertAsync(key1, lockId1, TimeSpan.FromMinutes(5));
        await fixture.LockStorage.InsertAsync(key2, lockId2, TimeSpan.FromMinutes(5));

        // when
        var result = await fixture.LockStorage.GetAllByPrefixAsync(prefix);

        // then
        result.Should().HaveCount(2);
        result.Should().ContainKey(key1).WhoseValue.Should().Be(lockId1);
        result.Should().ContainKey(key2).WhoseValue.Should().Be(lockId2);
    }

    [Fact]
    public async Task should_get_count_by_prefix()
    {
        // given
        var prefix = $"count-prefix-{Faker.Random.AlphaNumeric(5)}:";
        var key1 = $"{prefix}resource1";
        var key2 = $"{prefix}resource2";
        var key3 = $"{prefix}resource3";
        await fixture.LockStorage.InsertAsync(key1, "id1", TimeSpan.FromMinutes(5));
        await fixture.LockStorage.InsertAsync(key2, "id2", TimeSpan.FromMinutes(5));
        await fixture.LockStorage.InsertAsync(key3, "id3", TimeSpan.FromMinutes(5));

        // when
        var count = await fixture.LockStorage.GetCountAsync(prefix);

        // then
        count.Should().Be(3);
    }

    #endregion
}
