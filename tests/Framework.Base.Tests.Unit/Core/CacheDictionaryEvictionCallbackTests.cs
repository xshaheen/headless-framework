using Framework.Core;

namespace Tests.Core;

public sealed class CacheDictionaryEvictionCallbackTests : IDisposable
{
    private readonly List<string> _evictedKeys = [];
    private readonly CacheDictionary<string, string> _cache;

    public CacheDictionaryEvictionCallbackTests()
    {
        _cache = new(cleanupJobInterval: 100, itemEvicted: (key, _) => _evictedKeys.Add(key));
    }

    [Fact]
    public async Task should_fire_eviction_callback_when_item_expires()
    {
        // given
        var key = "test-key";
        _cache.AddOrUpdate(key, "value", TimeSpan.FromMilliseconds(1));

        // when
        await Task.Delay(120); // Wait for expiration background job

        // then
        _evictedKeys.Should().HaveCount(1);
        _evictedKeys[0].Should().Be(key);
    }

    [Fact]
    public async Task should_fire_callback_for_each_item_when_multiple_items_expire()
    {
        // given
        var keys = new[] { "key1", "key2", "key3" };
        foreach (var key in keys)
        {
            _cache.AddOrUpdate(key, "value", TimeSpan.FromMilliseconds(1));
        }

        // when
        await Task.Delay(5); // Wait for 1ms expiration
        _cache.EvictExpired();
        await Task.Delay(5); // Wait for callback to finish on another thread

        // then
        _evictedKeys.Should().BeEquivalentTo(keys);
    }

    [Fact]
    public void should_not_fire_eviction_callback_when_item_not_expired()
    {
        // given
        _cache.AddOrUpdate("key", "value", TimeSpan.FromMinutes(1));

        // when
        _cache.EvictExpired();

        // then
        _evictedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fire_callback_when_automatic_cleanup_occurs()
    {
        // given
        _cache.AddOrUpdate("key", "value", TimeSpan.FromMilliseconds(1));

        // when
        await Task.Delay(120); // Wait for cleanup job

        // then
        _evictedKeys.Should().HaveCount(1);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
