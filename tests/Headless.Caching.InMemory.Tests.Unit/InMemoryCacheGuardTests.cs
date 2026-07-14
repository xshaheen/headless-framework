// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>Guard contracts of the <see cref="InMemoryCache"/> public surface: cancellation, disposal, and argument validation.</summary>
public sealed class InMemoryCacheGuardTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private InMemoryCache _CreateCache() => new(_timeProvider, new InMemoryCacheOptions());

    #region Cancellation

    [Fact]
    public async Task should_throw_operation_canceled_when_token_already_cancelled()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var get = () => cache.GetAsync<string>(key, cts.Token).AsTask();
        var upsert = () => cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), cts.Token).AsTask();
        var remove = () => cache.RemoveAsync(key, cts.Token).AsTask();

        // then - every public entry point observes the token before doing any work
        await get.Should().ThrowAsync<OperationCanceledException>();
        await upsert.Should().ThrowAsync<OperationCanceledException>();
        await remove.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task get_or_add_should_throw_without_invoking_factory_when_token_already_cancelled()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        var factoryCalls = 0;
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = () =>
            cache
                .GetOrAddAsync<string>(
                    key,
                    _ =>
                    {
                        factoryCalls++;
                        return ValueTask.FromResult<string?>("value");
                    },
                    options,
                    cts.Token
                )
                .AsTask();

        // then - a dead token must not trigger factory (stampede) work
        await act.Should().ThrowAsync<OperationCanceledException>();
        factoryCalls.Should().Be(0);
    }

    #endregion

    #region Disposal

    [Fact]
    public async Task should_throw_object_disposed_when_used_after_dispose()
    {
        // given
        var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);
        await cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);

        // when
        cache.Dispose();

        // then - the disposal contract for a shared singleton: fail fast, not silently misbehave
        var get = () => cache.GetAsync<string>(key, AbortToken).AsTask();
        var upsert = () => cache.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken).AsTask();
        var remove = () => cache.RemoveAsync(key, AbortToken).AsTask();

        await get.Should().ThrowAsync<ObjectDisposedException>();
        await upsert.Should().ThrowAsync<ObjectDisposedException>();
        await remove.Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region Argument Validation

    [Fact]
    public async Task upsert_should_reject_negative_expiration()
    {
        // given - zero has dedicated evict semantics; negative must be rejected outright
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var act = () => cache.UpsertAsync(key, "value", TimeSpan.FromSeconds(-1), AbortToken).AsTask();

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task try_insert_should_reject_negative_expiration()
    {
        // given
        using var cache = _CreateCache();
        var key = Faker.Random.AlphaNumeric(10);

        // when
        var act = () => cache.TryInsertAsync(key, "value", TimeSpan.FromSeconds(-1), AbortToken).AsTask();

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_reject_null_or_empty_key(string? key)
    {
        // given - the Ordinal-keyed store relies on non-empty keys
        using var cache = _CreateCache();

        // when
        var get = () => cache.GetAsync<string>(key!, AbortToken).AsTask();
        var upsert = () => cache.UpsertAsync(key!, "value", TimeSpan.FromMinutes(5), AbortToken).AsTask();

        // then
        await get.Should().ThrowAsync<ArgumentException>();
        await upsert.Should().ThrowAsync<ArgumentException>();
    }

    #endregion
}
