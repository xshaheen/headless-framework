// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class DataKeyCacheTests : TestBase
{
    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public void should_keep_a_leased_key_intact_after_eviction_until_the_lease_ends()
    {
        using var cache = _Cache(size: 10);
        var key = Enumerable.Repeat((byte)7, 32).ToArray();
        var stored = _Stored("scope-a");

        var lease = cache.Add(stored, key, cache.Generation)!;
        cache.ForgetScope("scope-a");

        cache.TryAcquire(stored.KeyId).Should().BeNull();
        lease.Key.ToArray().Should().OnlyContain(b => b == 7);

        lease.Dispose();

        key.Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void should_drop_an_entry_once_it_expires()
    {
        using var cache = _Cache(size: 10);
        var key = new byte[32];
        var stored = _Stored("scope-a");
        cache.Add(stored, key, cache.Generation)!.Dispose();
        cache.SetActive("scope-a", DataKeyKind.Data, stored.KeyId, cache.Generation);

        _clock.Advance(TimeSpan.FromMinutes(1));

        cache.TryAcquire(stored.KeyId).Should().BeNull();
        cache.GetActive("scope-a", DataKeyKind.Data).Should().BeNull();
    }

    [Fact]
    public void should_stay_within_its_size_by_dropping_the_earliest_keys()
    {
        using var cache = _Cache(size: 2);
        var first = _Stored("a");
        var second = _Stored("b");
        var third = _Stored("c");

        cache.Add(first, new byte[32], cache.Generation)!.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(1));
        cache.Add(second, new byte[32], cache.Generation)!.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(1));
        cache.Add(third, new byte[32], cache.Generation)!.Dispose();

        cache.Count.Should().Be(2);
        cache.TryAcquire(first.KeyId).Should().BeNull();
        using var kept = cache.TryAcquire(third.KeyId);
        kept.Should().NotBeNull();
    }

    [Fact]
    public void should_release_a_lease_once_when_disposed_twice()
    {
        using var cache = _Cache(size: 10);
        var key = Enumerable.Repeat((byte)7, 32).ToArray();
        var stored = _Stored("scope-a");
        cache.Add(stored, key, cache.Generation)!.Dispose();
        var lease = cache.TryAcquire(stored.KeyId)!;

        lease.Dispose();
        lease.Dispose();

        key.Should().OnlyContain(b => b == 7, "the cache still holds its own reference");
    }

    [Fact]
    public void should_refuse_and_clear_a_key_read_before_its_scope_was_shredded()
    {
        using var cache = _Cache(size: 10);
        var key = Enumerable.Repeat((byte)7, 32).ToArray();
        var stored = _Stored("scope-a");
        var readBeforeShred = cache.Generation;

        cache.ForgetScope("scope-a");

        cache.Add(stored, key, readBeforeShred).Should().BeNull();
        key.Should().OnlyContain(b => b == 0);
        cache.TryAcquire(stored.KeyId).Should().BeNull();

        cache.SetActive("scope-a", DataKeyKind.Data, stored.KeyId, readBeforeShred);
        cache.GetActive("scope-a", DataKeyKind.Data).Should().BeNull();
    }

    [Fact]
    public void should_cache_keys_of_a_shredded_scope_read_after_the_shred()
    {
        using var cache = _Cache(size: 10);
        cache.ForgetScope("scope-a");
        var stored = _Stored("scope-a");

        using var lease = cache.Add(stored, new byte[32], cache.Generation);

        using var other = cache.Add(_Stored("scope-b"), new byte[32], generation: 0);

        lease.Should().NotBeNull();
        other.Should().NotBeNull("only the shredded scope is refused");
    }

    private DataKeyCache _Cache(int size) =>
        new(_clock, new EncryptionOptions { KeyCacheDuration = TimeSpan.FromMinutes(1), KeyCacheSize = size });

    private static StoredDataKey _Stored(string scope) =>
        new()
        {
            KeyId = Guid.CreateVersion7(),
            Scope = scope,
            Kind = DataKeyKind.Data,
            Version = 1,
            KekProvider = "Local",
            WrappedKey = new WrappedDataKey { KekId = "k", Ciphertext = [] },
        };
}
