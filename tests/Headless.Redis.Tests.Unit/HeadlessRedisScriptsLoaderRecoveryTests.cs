// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tests;

public sealed class HeadlessRedisScriptsLoaderRecoveryTests : TestBase
{
    [Fact]
    public async Task should_recover_from_noscript_error_by_reloading_once()
    {
        // given
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();

        // Use a concrete EndPoint since NSubstitute for abstract types with no parameterless constructor is tricky
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);

        multiplexer.GetEndPoints().Returns([endpoint]);
        multiplexer.GetServer(endpoint).Returns(server);
        multiplexer.GetDatabase().Returns(db);

        using var sut = new HeadlessRedisScriptsLoader(
            multiplexer,
            timeProvider: null,
            logger: LoggerFactory.CreateLogger<HeadlessRedisScriptsLoader>()
        );

        // First call fails with NOSCRIPT
        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns(
                _ => throw new RedisServerException("NOSCRIPT No matching script. Please use SCRIPT LOAD."),
                _ => Task.FromResult(RedisResult.Create(1)) // Success on retry
            );

        // when
        var result = await sut.ReplaceIfEqualAsync(db, "key", "expected", "new");

        // then
        result.Should().BeTrue();

        // Verify script was loaded twice (initial + once after NOSCRIPT)
        // Each load triggers 5 ScriptLoadAsync calls (one for each script in the loader)
        await server.Received(10).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());

        // Verify evaluate was called twice
        await db.Received(2).ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>());
    }

    [Fact]
    public async Task should_propagate_error_if_retry_also_fails_with_noscript()
    {
        // given
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);

        multiplexer.GetEndPoints().Returns([endpoint]);
        multiplexer.GetServer(endpoint).Returns(server);

        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // All calls fail with NOSCRIPT
        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns<Task<RedisResult>>(_ => throw new RedisServerException("NOSCRIPT Still no script."));

        // when
        var act = () => sut.ReplaceIfEqualAsync(db, "key", "expected", "new");

        // then
        await act.Should().ThrowAsync<RedisServerException>().WithMessage("NOSCRIPT*");

        // Verify it didn't infinite loop - should try twice (initial + 1 retry)
        await db.Received(2).ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>());
    }
}
