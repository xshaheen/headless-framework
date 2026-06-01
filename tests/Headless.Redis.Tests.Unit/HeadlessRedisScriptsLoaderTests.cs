// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Redis;
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
    public async Task should_not_load_any_script_when_preload_list_is_empty()
    {
        // given
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // when
        await sut.LoadAsync([]);

        // then
        await server.DidNotReceive().ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task should_keep_loaded_script_state_per_loader_instance()
    {
        // given
        var script = CustomReturnOneScriptDefinition.Instance;
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        var db = Substitute.For<IDatabase>();
        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // when
        using var loaded = new HeadlessRedisScriptsLoader(multiplexer);
        await loaded.LoadAsync([script]);
        _ = await loaded.EvaluateAsync(db, script, parameters: null);

        using var fresh = new HeadlessRedisScriptsLoader(multiplexer);
        _ = await fresh.EvaluateAsync(db, script, parameters: null);

        // then
        await server.Received(2).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task should_fail_fast_when_no_writable_endpoint_is_available()
    {
        // given
        var (multiplexer, _) = _CreateMultiplexerWithServer(isConnected: false, isReplica: false);
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // when
        var act = () => sut.LoadAsync([ReplaceIfEqualScriptDefinition.Instance]).AsTask();

        // then
        await act.Should().ThrowAsync<RedisConnectionException>().WithMessage("No writable Redis endpoints*");
    }

    [Fact]
    public async Task should_load_requested_script_definitions()
    {
        // given
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // when
        await sut.LoadAsync(RedisCacheScripts.Definitions);

        // then
        await server.Received(RedisCacheScripts.Definitions.Count).ScriptLoadAsync(
            Arg.Any<string>(),
            Arg.Any<CommandFlags>()
        );
    }

    [Fact]
    public async Task should_dedupe_duplicate_script_definitions_when_loading()
    {
        // given
        var script = CustomReturnOneScriptDefinition.Instance;
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // when
        await sut.LoadAsync([script, script]);

        // then
        await server.Received(1).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task should_evaluate_any_script_definition_on_demand()
    {
        // given
        var customScript = CustomReturnOneScriptDefinition.Instance;
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        var db = Substitute.For<IDatabase>();
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        // when
        var result = await sut.EvaluateAsync(db, customScript, parameters: null);

        // then
        ((int)result).Should().Be(1);
        await server.Received(1).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
        await db.Received(1).ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>());
    }

    private static (IConnectionMultiplexer Multiplexer, IServer Server) _CreateMultiplexerWithServer(
        bool isConnected,
        bool isReplica
    )
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var server = Substitute.For<IServer>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 6379);

        multiplexer.GetEndPoints().Returns([endpoint]);
        multiplexer.GetServer(endpoint).Returns(server);
        server.IsConnected.Returns(isConnected);
        server.IsReplica.Returns(isReplica);

        return (multiplexer, server);
    }

    private sealed class CustomReturnOneScriptDefinition : RedisScriptDefinition
    {
        public static CustomReturnOneScriptDefinition Instance { get; } = new();

        private CustomReturnOneScriptDefinition()
            : base("return 1") { }
    }
}
