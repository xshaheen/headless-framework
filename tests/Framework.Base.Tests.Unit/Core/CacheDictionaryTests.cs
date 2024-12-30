using Framework.Core;

namespace Tests.Core;

public sealed class CacheDictionaryTests
{
    [Fact]
    public void should_add_new_item_successfully_when_add_or_update_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();

        // when
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMinutes(1));
        var exists = cache.TryGet("key1", out var value);

        // then
        exists.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void should_update_existing_item_successfully_when_add_or_update_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMinutes(1));

        // when
        cache.AddOrUpdate("key1", 43, TimeSpan.FromMinutes(1));
        var exists = cache.TryGet("key1", out var value);

        // then
        exists.Should().BeTrue();
        value.Should().Be(43);
    }

    [Fact]
    public async Task should_return_false_when_try_get_is_called_on_expired_item()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMilliseconds(100));

        // when
        await Task.Delay(200); // Wait for expiration
        var exists = cache.TryGet("key1", out var value);

        // then
        exists.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public void should_return_true_when_try_add_is_called_with_new_item()
    {
        // given
        using var cache = new CacheDictionary<string, int>();

        // when
        var added = cache.TryAdd("key1", 42, TimeSpan.FromMinutes(1));

        // then
        added.Should().BeTrue();
        cache.TryGet("key1", out var value).Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void should_return_false_when_try_add_is_called_with_existing_item()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMinutes(1));

        // when
        var added = cache.TryAdd("key1", 43, TimeSpan.FromMinutes(1));

        // then
        added.Should().BeFalse();
        cache.TryGet("key1", out var value).Should().BeTrue();
        value.Should().Be(42); // Original value should remain
    }

    [Fact]
    public void should_add_and_return_value_when_get_or_add_is_called_with_new_item()
    {
        // given
        using var cache = new CacheDictionary<string, int>();

        // when
        var value = cache.GetOrAdd("key1", k => 42, TimeSpan.FromMinutes(1));

        // then
        value.Should().Be(42);
        cache.TryGet("key1", out var retrieved).Should().BeTrue();
        retrieved.Should().Be(42);
    }

    [Fact]
    public void should_return_existing_value_when_get_or_add_is_called_on_existing_non_expired_item()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMinutes(1));

        // when
        var value = cache.GetOrAdd("key1", k => 43, TimeSpan.FromMinutes(1));

        // then
        value.Should().Be(42); // Should return existing value
    }

    [Fact]
    public async Task should_return_new_value_when_get_or_add_is_called_on_existing_expired_item()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMilliseconds(100));
        await Task.Delay(200); // Wait for expiration

        // when
        var value = cache.GetOrAdd("key1", k => 43, TimeSpan.FromMinutes(1));

        // then
        value.Should().Be(43); // Should return new value
    }

    [Fact]
    public void should_add_and_return_value_when_get_or_add_with_arg_is_called_with_new_item()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        var multiplier = 2;

        // when
        var value = cache.GetOrAdd("key1", (k, m) => 21 * m, TimeSpan.FromMinutes(1), multiplier);

        // then
        value.Should().Be(42);
    }

    [Fact]
    public void should_remove_existing_item_successfully_when_remove_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMinutes(1));

        // when
        cache.Remove("key1");

        // then
        cache.TryGet("key1", out _).Should().BeFalse();
    }

    [Fact]
    public void should_remove_and_return_value_when_try_remove_is_called_on_existing_item()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMinutes(1));

        // when
        var removed = cache.TryRemove("key1", out var value);

        // then
        removed.Should().BeTrue();
        value.Should().Be(42);
        cache.TryGet("key1", out _).Should().BeFalse();
    }

    [Fact]
    public void should_remove_all_items_when_clear_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMinutes(1));
        cache.AddOrUpdate("key2", 43, TimeSpan.FromMinutes(1));

        // when
        cache.Clear();

        // then
        cache.Count.Should().Be(0);
        cache.TryGet("key1", out _).Should().BeFalse();
        cache.TryGet("key2", out _).Should().BeFalse();
    }

    [Fact]
    public void should_return_only_non_expired_items_when_enumeration_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMinutes(1));
        cache.AddOrUpdate("key2", 43, TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50); // Wait for second item to expire

        // when
        var items = cache.ToList();

        // then
        items.Should().ContainSingle();
        items[0].Value.Should().Be(42);
        items[0].Key.Should().Be("key1");
    }

    [Fact]
    public async Task should_remove_expired_items_when_evict_expired_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("key1", 42, TimeSpan.FromMilliseconds(100));
        cache.AddOrUpdate("key2", 43, TimeSpan.FromMinutes(1));

        // when
        await Task.Delay(200); // Wait for first item to expire
        cache.EvictExpired();

        // then
        cache.TryGet("key1", out _).Should().BeFalse();
        cache.TryGet("key2", out var value).Should().BeTrue();
        value.Should().Be(43);
    }

    [Fact]
    public async Task should_cleanup_and_expire_item_when_cleanup_job_runs()
    {
        // given
        using var cache = new CacheDictionary<int, int>(cleanupJobInterval: 200);
        cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(100));

        // when
        var existsInitially = cache.TryGet(42, out var valueInitially);
        await Task.Delay(300);

        // then
        existsInitially.Should().BeTrue();
        valueInitially.Should().Be(42);
        cache.Count.Should().Be(0); // cleanup job has run
    }

    [Fact]
    public async Task should_cleanup_all_cache_items_when_eviction_occurs()
    {
        // given
        var list = new List<CacheDictionary<int, int>>();
        for (var i = 0; i < 20; i++)
        {
            var cache = new CacheDictionary<int, int>(cleanupJobInterval: 200);
            cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(100));
            list.Add(cache);
        }
        await Task.Delay(300);

        // then
        foreach (var cache in list)
        {
            cache.Count.Should().Be(0); // cleanup job has run
            cache.Dispose();
        }
    }

    [Fact]
    public async Task should_not_evicted_item_when_short_delay()
    {
        // given
        using var cache = new CacheDictionary<int, int>();
        cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(500));

        // when
        await Task.Delay(50);
        var exists = cache.TryGet(42, out var result);

        // then
        exists.Should().BeTrue();
        result.Should().Be(42);
    }

    [Fact]
    public async Task should_expire_item_when_default_cleanup_interval()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));

        // when
        var existsInitially = cache.TryGet("42", out _);
        await Task.Delay(150);
        var existsAfterDelay = cache.TryGet("42", out _);

        // then
        existsInitially.Should().BeTrue();
        existsAfterDelay.Should().BeFalse();
    }

    [Fact]
    public void should_remove_item_when_remove_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));

        // when
        cache.Remove("42");

        // then
        cache.TryGet("42", out _).Should().BeFalse();
    }

    [Fact]
    public void should_try_remove_item_and_return_value()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));

        // when
        var res = cache.TryRemove("42", out var value);

        // then
        res.Should().BeTrue();
        value.Should().Be(42);
        cache.TryGet("42", out _).Should().BeFalse();

        // now try remove non-existing item
        res = cache.TryRemove("nonexistent", out value);
        res.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public async Task should_not_remove_expired_item_when_try_remove_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.AddOrUpdate("42", 42, TimeSpan.FromMilliseconds(100));
        await Task.Delay(120); // let the item expire

        // when
        var res = cache.TryRemove("42", out var value);

        // then
        res.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public async Task should_try_add_item_and_handle_expiration()
    {
        // given
        using var cache = new CacheDictionary<string, int>();

        // when
        var addedInitially = cache.TryAdd("42", 42, TimeSpan.FromMilliseconds(100));
        var addedAgain = cache.TryAdd("42", 42, TimeSpan.FromMilliseconds(100));
        await Task.Delay(120); // wait for it to expire
        var addedAfterExpiration = cache.TryAdd("42", 42, TimeSpan.FromMilliseconds(100));

        // then
        addedInitially.Should().BeTrue();
        addedAgain.Should().BeFalse();
        addedAfterExpiration.Should().BeTrue();
    }

    [Fact]
    public async Task should_get_or_add_item_and_handle_expiration()
    {
        // given
        using var cache = new CacheDictionary<string, int>();

        // when
        cache.GetOrAdd("key", k => 1024, TimeSpan.FromMilliseconds(100));
        var value1 = cache.GetOrAdd("key", k => 1025, TimeSpan.FromMilliseconds(100));
        var exists = cache.TryGet("key", out var res);
        await Task.Delay(110);
        var existsAfterExpiration = cache.TryGet("key", out _);

        // then
        value1.Should().Be(1024); // old value
        exists.Should().BeTrue();
        res.Should().Be(1024); // another way to retrieve
        existsAfterExpiration.Should().BeFalse();

        // now try non-factory overloads
        var value2 = cache.GetOrAdd("key123", 123321, TimeSpan.FromMilliseconds(100));
        var value3 = cache.GetOrAdd("key123", -1, TimeSpan.FromMilliseconds(100));
        await Task.Delay(110);
        var value4 = cache.GetOrAdd("key123", -1, TimeSpan.FromMilliseconds(100));

        value2.Should().Be(123321);
        value3.Should().Be(123321); // still old value
        value4.Should().Be(-1); // new value
    }

    [Fact]
    public async Task should_get_or_add_item_with_arg_and_handle_eviction()
    {
        // given
        using var cache = new CacheDictionary<string, int>();

        // when
        cache.GetOrAdd("key", (k, arg) => 1024 + arg.Length, TimeSpan.FromMilliseconds(100), "test123");
        var existsInitially = cache.TryGet("key", out var res);
        await Task.Delay(110);
        var existsAfterDelay = cache.TryGet("key", out _);

        // then
        existsInitially.Should().BeTrue();
        res.Should().Be(1031);
        existsAfterDelay.Should().BeFalse();

        // now try without TryGet
        var value1 = cache.GetOrAdd("key2", (k, arg) => 21 + arg.Length, TimeSpan.FromMilliseconds(100), "123");
        var value2 = cache.GetOrAdd("key2", (k, arg) => 2211 + arg.Length, TimeSpan.FromMilliseconds(100), "123");
        await Task.Delay(110);
        var value3 = cache.GetOrAdd("key2", (k, arg) => 2211 + arg.Length, TimeSpan.FromMilliseconds(100), "123");

        value1.Should().Be(24);
        value2.Should().Be(24); // still old value
        value3.Should().Be(2214); // new value
    }

    [Fact]
    public void should_clear_cache_when_clear_is_called()
    {
        // given
        using var cache = new CacheDictionary<string, int>();
        cache.GetOrAdd("key", _ => 1024, TimeSpan.FromSeconds(100));

        // when
        cache.Clear();

        // then
        cache.TryGet("key", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_enforce_atomicity_when_try_adding_items_concurrently()
    {
        // given
        var i = 0;
        using var cache = new CacheDictionary<int, int>();
        cache.TryAdd(42, 42, TimeSpan.FromMilliseconds(50)); // add item with short TTL
        await Task.Delay(100); // wait for the value to expire

        // when
        await _RunConcurrently(
            20,
            () =>
            {
                if (cache.TryAdd(42, 42, TimeSpan.FromSeconds(1)))
                {
                    i++;
                }
            }
        );

        // then
        i.Should().Be(1, i.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task should_enforce_atomicity_when_getting_or_adding_items_concurrently()
    {
        // given
        var i = 0;
        using var cache = new CacheDictionary<int, int>();
        cache.GetOrAdd(42, 42, TimeSpan.FromMilliseconds(100));
        await Task.Delay(110); // wait for the value to expire

        // when
        await _RunConcurrently(20, () => cache.GetOrAdd(42, _ => ++i, TimeSpan.FromSeconds(1)));

        // then
        cache.TryGet(42, out i).Should().BeTrue();
        i.Should().Be(1, i.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task should_enumerate_items_correctly()
    {
        // given
        using var cache = new CacheDictionary<string, int>(); // now with default cleanup interval
        cache.GetOrAdd("key", _ => 1024, TimeSpan.FromMilliseconds(100));

        // when
        var firstItem = cache.FirstOrDefault().Value;
        await Task.Delay(105);

        // then
        firstItem.Should().Be(1024);
        cache.Count.Should().Be(1); // Because cleanup job has not run yet
    }

    [Fact]
    public async Task should_extend_ttl_when_item_is_updated()
    {
        // given
        using var cache = new CacheDictionary<int, int>();
        cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(300));

        // when
        await Task.Delay(50);
        var existsInitially = cache.TryGet(42, out var resultInitially);
        cache.AddOrUpdate(42, 42, TimeSpan.FromMilliseconds(300));
        await Task.Delay(250);
        var existsAfterUpdate = cache.TryGet(42, out var resultAfterUpdate);

        // then
        existsInitially.Should().BeTrue();
        resultInitially.Should().Be(42);
        existsAfterUpdate.Should().BeTrue();
        resultAfterUpdate.Should().Be(42);
    }

    private static async Task _RunConcurrently(int numThreads, Action action)
    {
        var tasks = new Task[numThreads];
        using var m = new ManualResetEvent(false);

        for (var i = 0; i < numThreads; i++)
        {
            // ReSharper disable once AccessToDisposedClosure
            tasks[i] = Task.Run(() =>
            {
                m.WaitOne(); // Dont start just yet
                action();
            });
        }

        m.Set(); // Off we go

        await Task.WhenAll(tasks);
    }
}
