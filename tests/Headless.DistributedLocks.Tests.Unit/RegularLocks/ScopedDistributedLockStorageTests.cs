// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests.RegularLocks;

public sealed class ScopedDistributedLockStorageTests : TestBase
{
    private const string _Prefix = "distributed-lock:";
    private readonly IDistributedLockStorage _inner = Substitute.For<IDistributedLockStorage>();

    private ScopedDistributedLockStorage _CreateStorage() => new(_inner, _Prefix);

    [Fact]
    public void constructor_should_reject_null_inner_and_empty_prefix()
    {
        // when / then
        var nullInner = () => new ScopedDistributedLockStorage(null!, _Prefix);
        nullInner.Should().Throw<ArgumentNullException>().WithParameterName("inner");

        var emptyPrefix = () => new ScopedDistributedLockStorage(_inner, "");
        emptyPrefix.Should().Throw<ArgumentException>().WithParameterName("scopedPrefix");
    }

    [Fact]
    public async Task write_operations_should_prepend_the_scope_prefix()
    {
        // given
        var storage = _CreateStorage();

        // when
        await storage.InsertAsync("resource", "lease-1", TimeSpan.FromSeconds(30), AbortToken);
        await storage.ReplaceIfEqualAsync("resource", "lease-1", "lease-2", TimeSpan.FromSeconds(30), AbortToken);
        await storage.RemoveIfEqualAsync("resource", "lease-2", AbortToken);

        // then — every key reaching the backend is namespaced
        await _inner.Received(1).InsertAsync($"{_Prefix}resource", "lease-1", TimeSpan.FromSeconds(30), AbortToken);
        await _inner
            .Received(1)
            .ReplaceIfEqualAsync($"{_Prefix}resource", "lease-1", "lease-2", TimeSpan.FromSeconds(30), AbortToken);
        await _inner.Received(1).RemoveIfEqualAsync($"{_Prefix}resource", "lease-2", AbortToken);
    }

    [Fact]
    public async Task read_operations_should_prepend_the_scope_prefix()
    {
        // given
        var storage = _CreateStorage();

        // when
        await storage.ExistsAsync("resource", AbortToken);
        await storage.GetAsync("resource", AbortToken);
        await storage.GetExpirationAsync("resource", AbortToken);
        await storage.GetCountAsync("res", AbortToken);

        // then
        await _inner.Received(1).ExistsAsync($"{_Prefix}resource", AbortToken);
        await _inner.Received(1).GetAsync($"{_Prefix}resource", AbortToken);
        await _inner.Received(1).GetExpirationAsync($"{_Prefix}resource", AbortToken);
        await _inner.Received(1).GetCountAsync($"{_Prefix}res", AbortToken);
    }

    [Fact]
    public async Task get_all_by_prefix_should_strip_the_scope_prefix_from_returned_keys()
    {
        // given — the backend returns fully-scoped keys; callers must see bare resource names
        var storage = _CreateStorage();
        _inner
            .GetAllByPrefixAsync($"{_Prefix}res", AbortToken)
            .Returns(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [$"{_Prefix}res-1"] = "lease-1",
                    [$"{_Prefix}res-2"] = "lease-2",
                }
            );

        // when
        var result = await storage.GetAllByPrefixAsync("res", AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().ContainKey("res-1").WhoseValue.Should().Be("lease-1");
        result.Should().ContainKey("res-2").WhoseValue.Should().Be("lease-2");
    }

    [Fact]
    public async Task get_all_with_expiration_by_prefix_should_strip_the_scope_prefix_from_returned_keys()
    {
        // given
        var storage = _CreateStorage();
        var ttl = TimeSpan.FromSeconds(42);
        _inner
            .GetAllWithExpirationByPrefixAsync($"{_Prefix}res", AbortToken)
            .Returns(
                new Dictionary<string, (string LeaseId, TimeSpan? Ttl)>(StringComparer.Ordinal)
                {
                    [$"{_Prefix}res-1"] = ("lease-1", ttl),
                }
            );

        // when
        var result = await storage.GetAllWithExpirationByPrefixAsync("res", AbortToken);

        // then
        result.Should().ContainSingle();
        result.Should().ContainKey("res-1");
        result["res-1"].LeaseId.Should().Be("lease-1");
        result["res-1"].Ttl.Should().Be(ttl);
    }
}
