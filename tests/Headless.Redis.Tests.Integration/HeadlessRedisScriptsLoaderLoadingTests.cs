// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using Headless.Testing.Tests;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class HeadlessRedisScriptsLoaderLoadingTests(RedisTestFixture fixture) : TestBase
{
    [Fact]
    public async Task LoadAsync_should_load_requested_bundle()
    {
        // given
        using var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);
        RedisScriptDefinition[] scripts =
        [
            TestReturnOneScriptDefinition.Instance,
            TestReturnTwoScriptDefinition.Instance,
        ];

        // when
        var act = async () => await loader.LoadAsync(scripts, cancellationToken: AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LoadAsync_should_be_idempotent()
    {
        // given
        using var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);
        RedisScriptDefinition[] scripts =
        [
            TestReturnOneScriptDefinition.Instance,
            TestReturnTwoScriptDefinition.Instance,
        ];

        // when
        await loader.LoadAsync(scripts, cancellationToken: AbortToken);
        var act = async () => await loader.LoadAsync(scripts, cancellationToken: AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EvaluateAsync_should_evaluate_preloaded_script()
    {
        // given
        using var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var key = (RedisKey)("loader-evaluate-preloaded:" + Guid.NewGuid().ToString("N"));
        await loader.LoadAsync([TestSetScriptDefinition.Instance], cancellationToken: AbortToken);

        // when
        var result = await loader.EvaluateAsync(
            db,
            TestSetScriptDefinition.Instance,
            new { key, value = "2" },
            cancellationToken: AbortToken
        );

        // then
        ((int)result)
            .Should()
            .Be(1);
        (await db.StringGetAsync(key)).ToString().Should().Be("2");
    }

    [Fact]
    public async Task EvaluateAsync_should_recover_via_eval_after_server_script_cache_flush()
    {
        // given — preload + a first eval so the script is cached on the server (EVALSHA path)
        using var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var key = (RedisKey)("loader-eval-after-flush:" + Guid.NewGuid().ToString("N"));
        await loader.LoadAsync([TestSetScriptDefinition.Instance], cancellationToken: AbortToken);

        var parameters = new { key, value = "1" };

        (
            (int)
                await loader.EvaluateAsync(
                    db,
                    TestSetScriptDefinition.Instance,
                    parameters,
                    cancellationToken: AbortToken
                )
        )
            .Should()
            .Be(1);

        // when — the serving node loses the script from its cache (simulates a promoted replica or a
        // cold/flushed cache). The loader still holds the stale SHA, so the next EVALSHA gets NOSCRIPT.
        foreach (var endpoint in fixture.ConnectionMultiplexer.GetEndPoints())
        {
            await fixture.ConnectionMultiplexer.GetServer(endpoint).ScriptFlushAsync();
        }

        // then — recovery falls back to a full-body EVAL, so the script still runs
        var result = await loader.EvaluateAsync(
            db,
            TestSetScriptDefinition.Instance,
            parameters,
            cancellationToken: AbortToken
        );

        ((int)result).Should().Be(1);
    }

    private sealed class TestReturnOneScriptDefinition : RedisScriptDefinition
    {
        public static TestReturnOneScriptDefinition Instance { get; } = new();

        private TestReturnOneScriptDefinition()
            : base("return 1") { }
    }

    private sealed class TestReturnTwoScriptDefinition : RedisScriptDefinition
    {
        public static TestReturnTwoScriptDefinition Instance { get; } = new();

        private TestReturnTwoScriptDefinition()
            : base("return 2") { }
    }

    private sealed class TestSetScriptDefinition : RedisScriptDefinition
    {
        public static TestSetScriptDefinition Instance { get; } = new();

        private TestSetScriptDefinition()
            : base("return redis.call('set', @key, @value) and 1 or 0") { }
    }
}
