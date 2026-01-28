// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Tests;

[Collection(nameof(RedisTestFixture))]
public sealed class HeadlessRedisScriptsLoaderLoadingTests(RedisTestFixture fixture)
{
    [Fact]
    public async Task LoadScriptsAsync_should_load_all_scripts()
    {
        // given
        var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);

        // when
        var act = async () => await loader.LoadScriptsAsync();

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LoadScriptsAsync_should_populate_script_properties()
    {
        // given
        var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);

        // when
        await loader.LoadScriptsAsync();

        // then
        loader.IncrementWithExpireScript.Should().NotBeNull();
        loader.RemoveIfEqualScript.Should().NotBeNull();
        loader.ReplaceIfEqualScript.Should().NotBeNull();
        loader.SetIfHigherScript.Should().NotBeNull();
        loader.SetIfLowerScript.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadScriptsAsync_should_be_idempotent()
    {
        // given
        var loader = new HeadlessRedisScriptsLoader(fixture.ConnectionMultiplexer);

        // when
        await loader.LoadScriptsAsync();
        var act = async () => await loader.LoadScriptsAsync();

        // then
        await act.Should().NotThrowAsync();
        loader.IncrementWithExpireScript.Should().NotBeNull();
        loader.RemoveIfEqualScript.Should().NotBeNull();
        loader.ReplaceIfEqualScript.Should().NotBeNull();
        loader.SetIfHigherScript.Should().NotBeNull();
        loader.SetIfLowerScript.Should().NotBeNull();
    }
}
