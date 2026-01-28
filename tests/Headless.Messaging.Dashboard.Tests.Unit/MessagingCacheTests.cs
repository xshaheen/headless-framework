// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessagingCacheTests : TestBase
{
    [Fact]
    public void should_add_and_retrieve_items()
    {
        // given
        using var cache = new MessagingCache();
        const string key = "test-key";
        const string value = "test-value";

        // when
        cache.AddOrUpdate(key, value);

        // then
        cache.TryGet(key, out var result).Should().BeTrue();
        result.Should().Be(value);
    }

    [Fact]
    public void should_update_existing_item()
    {
        // given
        using var cache = new MessagingCache();
        const string key = "test-key";
        cache.AddOrUpdate(key, "old-value");

        // when
        cache.AddOrUpdate(key, "new-value");

        // then
        cache.TryGet(key, out var result).Should().BeTrue();
        result.Should().Be("new-value");
    }

    [Fact]
    public void should_return_false_for_missing_key()
    {
        // given
        using var cache = new MessagingCache();

        // when & then
        cache.TryGet("non-existent", out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void should_check_key_existence()
    {
        // given
        using var cache = new MessagingCache();
        cache.AddOrUpdate("existing", "value");

        // when & then
        cache.Exists("existing").Should().BeTrue();
        cache.Exists("non-existing").Should().BeFalse();
    }

    [Fact]
    public void should_remove_item()
    {
        // given
        using var cache = new MessagingCache();
        const string key = "test-key";
        cache.AddOrUpdate(key, "value");

        // when
        cache.Remove(key);

        // then
        cache.Exists(key).Should().BeFalse();
    }

    [Fact]
    public void should_clear_all_items()
    {
        // given
        using var cache = new MessagingCache();
        cache.AddOrUpdate("key1", "value1");
        cache.AddOrUpdate("key2", "value2");
        cache.AddOrUpdate("key3", "value3");

        // when
        cache.Clear();

        // then
        cache.Exists("key1").Should().BeFalse();
        cache.Exists("key2").Should().BeFalse();
        cache.Exists("key3").Should().BeFalse();
    }

    [Fact]
    public async Task should_expire_item_after_timeout()
    {
        // given
        using var cache = new MessagingCache();
        const string key = "expiring-key";
        cache.AddOrUpdate(key, "value", TimeSpan.FromMilliseconds(100));

        // when - wait for expiration
        await Task.Delay(200, AbortToken);

        // then
        cache.Exists(key).Should().BeFalse();
    }

    [Fact]
    public void should_not_expire_item_with_infinite_timeout()
    {
        // given
        using var cache = new MessagingCache();
        const string key = "permanent-key";
        cache.AddOrUpdate(key, "value");

        // when & then - item should persist
        cache.Exists(key).Should().BeTrue();
        cache.TryGet(key, out var result).Should().BeTrue();
        result.Should().Be("value");
    }

    [Fact]
    public void should_remove_by_predicate()
    {
        // given
        using var cache = new MessagingCache();
        cache.AddOrUpdate("prefix.key1", "value1");
        cache.AddOrUpdate("prefix.key2", "value2");
        cache.AddOrUpdate("other.key3", "value3");

        // when
        cache.Remove(k => k.StartsWith("prefix.", StringComparison.Ordinal));

        // then
        cache.Exists("prefix.key1").Should().BeFalse();
        cache.Exists("prefix.key2").Should().BeFalse();
        cache.Exists("other.key3").Should().BeTrue();
    }

    [Fact]
    public void should_get_item_via_indexer()
    {
        // given
        using var cache = new MessagingCache();
        const string key = "test-key";
        const string value = "test-value";
        cache.AddOrUpdate(key, value);

        // when & then
        cache[key].Should().Be(value);
    }

    [Fact]
    public void should_return_null_for_missing_key_via_indexer()
    {
        // given
        using var cache = new MessagingCache();

        // when & then
        cache["non-existent"].Should().BeNull();
    }

    [Fact]
    public void Global_should_return_singleton_instance()
    {
        // given & when
        var instance1 = MessagingCache.Global;
        var instance2 = MessagingCache.Global;

        // then
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void should_not_throw_when_removing_non_existent_key()
    {
        // given
        using var cache = new MessagingCache();

        // when
        var act = () => cache.Remove("non-existent-key");

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_null_value()
    {
        // given
        using var cache = new MessagingCache();
        const string key = "null-key";

        // when
        cache.AddOrUpdate(key, null);

        // then
        cache.Exists(key).Should().BeTrue();
        cache[key].Should().BeNull();
    }

    [Fact]
    public void should_be_thread_safe_for_concurrent_operations()
    {
        // given
        using var cache = new MessagingCache();
        const int operationCount = 1000;

        // when
        Parallel.For(
            0,
            operationCount,
            i =>
            {
                var key = $"key-{i}";
                cache.AddOrUpdate(key, i);
                cache.TryGet(key, out _);
                cache.Exists(key);
            }
        );

        // then - no exceptions thrown, basic consistency check
        cache.Exists("key-0").Should().BeTrue();
    }

    [Fact]
    public void should_not_throw_after_dispose()
    {
        // given
        var cache = new MessagingCache();
        cache.AddOrUpdate("key", "value");

        // when
        cache.Dispose();

        // then - operations should not throw but return defaults
        cache.TryGet("key", out var result).Should().BeFalse();
        result.Should().BeNull();
        cache.Exists("key").Should().BeFalse();
    }
}
