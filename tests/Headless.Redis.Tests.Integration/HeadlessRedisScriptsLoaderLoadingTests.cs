// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using StackExchange.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class HeadlessRedisScriptsLoaderLoadingTests(RedisTestFixture fixture)
{
    [Fact]
    public async Task LoadAsync_should_load_requested_bundle()
    {
        // given
        using var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);
        RedisScriptDefinition[] scripts =
        [
            IncrementWithExpireScriptDefinition.Instance,
            RemoveIfEqualScriptDefinition.Instance,
            ReplaceIfEqualScriptDefinition.Instance,
            SetIfHigherScriptDefinition.Instance,
            SetIfLowerScriptDefinition.Instance,
        ];

        // when
        var act = async () => await loader.LoadAsync(scripts);

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
            IncrementWithExpireScriptDefinition.Instance,
            RemoveIfEqualScriptDefinition.Instance,
            ReplaceIfEqualScriptDefinition.Instance,
            SetIfHigherScriptDefinition.Instance,
            SetIfLowerScriptDefinition.Instance,
        ];

        // when
        await loader.LoadAsync(scripts);
        var act = async () => await loader.LoadAsync(scripts);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EvaluateAsync_should_evaluate_preloaded_script()
    {
        // given
        using var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);
        var db = fixture.ConnectionMultiplexer.GetDatabase();
        var key = (RedisKey)"loader-evaluate-preloaded";
        await db.KeyDeleteAsync(key);
        await loader.LoadAsync([IncrementWithExpireScriptDefinition.Instance]);

        // when
        var result = await loader.EvaluateAsync(
            db,
            IncrementWithExpireScriptDefinition.Instance,
            new
            {
                key,
                value = (RedisValue)1,
                expires = 60_000,
            }
        );

        // then
        ((long)result).Should().Be(1);
    }
}
