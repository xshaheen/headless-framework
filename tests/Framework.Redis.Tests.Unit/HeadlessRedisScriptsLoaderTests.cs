// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Redis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tests;

public sealed class HeadlessRedisScriptsLoaderTests
{
    [Fact]
    public void should_accept_null_timeProvider_and_use_system()
    {
        // given
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        // when
        var act = () => new HeadlessRedisScriptsLoader(multiplexer, timeProvider: null);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_accept_null_logger()
    {
        // given
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        // when
        var act = () => new HeadlessRedisScriptsLoader(multiplexer, logger: null);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_initialize_with_scripts_not_loaded()
    {
        // given
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        // when
        var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // then
        sut.IncrementWithExpireScript.Should().BeNull();
        sut.RemoveIfEqualScript.Should().BeNull();
        sut.ReplaceIfEqualScript.Should().BeNull();
        sut.SetIfHigherScript.Should().BeNull();
        sut.SetIfLowerScript.Should().BeNull();
    }
}
