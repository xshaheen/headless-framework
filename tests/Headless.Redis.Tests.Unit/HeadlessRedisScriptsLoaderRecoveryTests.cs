// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tests;

public sealed class HeadlessRedisScriptsLoaderRecoveryTests : TestBase
{
    private static object SampleParameters =>
        new
        {
            key = (RedisKey)"key",
            value = (RedisValue)"new",
            expected = (RedisValue)"expected",
            expires = RedisValue.EmptyString,
        };

    [Fact]
    public async Task should_recover_from_noscript_error_by_re_evaluating_with_eval()
    {
        // given
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);

        multiplexer.GetEndPoints().Returns([endpoint]);
        multiplexer.GetServer(endpoint).Returns(server);
        server.IsConnected.Returns(true);

        using var sut = new HeadlessRedisScriptsLoader(
            multiplexer,
            timeProvider: null,
            logger: LoggerFactory.CreateLogger<HeadlessRedisScriptsLoader>()
        );

        // The cached EVALSHA path (LoadedLuaScript) fails with NOSCRIPT — e.g. the serving node is
        // missing the script after a failover.
        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns<Task<RedisResult>>(_ =>
                throw new RedisServerException("NOSCRIPT No matching script. Please use SCRIPT LOAD.")
            );

        // The recovery path re-runs the full body via EVAL (LuaScript overload), which succeeds.
        db.ScriptEvaluateAsync(Arg.Any<LuaScript>(), Arg.Any<object>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // when
        var result = await sut.EvaluateAsync(db, SampleScriptDefinition.Instance, SampleParameters, AbortToken);

        // then
        ((int)result)
            .Should()
            .Be(1);

        // Recovery does NOT reload the script — it falls straight back to EVAL, so the script is
        // loaded only once (the initial preload) and the EVALSHA path is attempted only once.
        await server.Received(1).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
        await db.Received(1).ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>());

        // The EVAL recovery is invoked exactly once, with NoScriptCache so it cannot NOSCRIPT again.
        await db.Received(1).ScriptEvaluateAsync(Arg.Any<LuaScript>(), Arg.Any<object>(), CommandFlags.NoScriptCache);
    }

    [Fact]
    public async Task should_propagate_error_and_not_loop_when_eval_recovery_also_fails()
    {
        // given
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);

        multiplexer.GetEndPoints().Returns([endpoint]);
        multiplexer.GetServer(endpoint).Returns(server);
        server.IsConnected.Returns(true);

        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns<Task<RedisResult>>(_ => throw new RedisServerException("NOSCRIPT No matching script."));

        // Even the EVAL recovery fails (e.g. the connection drops mid-recovery).
        db.ScriptEvaluateAsync(Arg.Any<LuaScript>(), Arg.Any<object>(), Arg.Any<CommandFlags>())
            .Returns<Task<RedisResult>>(_ => throw new RedisServerException("LOADING Redis is loading the dataset."));

        // when
        var act = () => sut.EvaluateAsync(db, SampleScriptDefinition.Instance, SampleParameters, AbortToken);

        // then
        await act.Should().ThrowAsync<RedisServerException>();

        // The recovery is attempted exactly once — no retry loop.
        await db.Received(1).ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>());
        await db.Received(1).ScriptEvaluateAsync(Arg.Any<LuaScript>(), Arg.Any<object>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task should_not_attempt_recovery_when_error_is_not_noscript()
    {
        // given
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);

        multiplexer.GetEndPoints().Returns([endpoint]);
        multiplexer.GetServer(endpoint).Returns(server);
        server.IsConnected.Returns(true);

        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns<Task<RedisResult>>(_ => throw new RedisServerException("WRONGTYPE Operation against a key."));

        // when
        var act = () => sut.EvaluateAsync(db, SampleScriptDefinition.Instance, SampleParameters, AbortToken);

        // then — a non-NOSCRIPT error propagates immediately; the EVAL recovery path is never entered.
        await act.Should().ThrowAsync<RedisServerException>().WithMessage("WRONGTYPE*");
        await db.DidNotReceive().ScriptEvaluateAsync(Arg.Any<LuaScript>(), Arg.Any<object>(), Arg.Any<CommandFlags>());
    }

    // Arbitrary definition for driving the loader against a mocked database; the script body never
    // runs because ScriptEvaluateAsync is substituted, so any valid Lua source suffices.
    private sealed class SampleScriptDefinition : RedisScriptDefinition
    {
        public static SampleScriptDefinition Instance { get; } = new();

        private SampleScriptDefinition()
            : base("return 1") { }
    }
}
