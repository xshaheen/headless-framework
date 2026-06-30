// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
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
        var act = () => sut.LoadAsync([CustomReturnOneScriptDefinition.Instance]).AsTask();

        // then
        await act.Should().ThrowAsync<RedisConnectionException>().WithMessage("No writable Redis endpoints*");
    }

    [Fact]
    public async Task should_skip_replica_and_disconnected_endpoints_when_loading_requested_scripts()
    {
        // given
        RedisScriptDefinition[] scripts =
        [
            CustomReturnOneScriptDefinition.Instance,
            CustomReturnTwoScriptDefinition.Instance,
        ];
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var writableEndpoint = new IPEndPoint(IPAddress.Loopback, 6379);
        var replicaEndpoint = new IPEndPoint(IPAddress.Loopback, 6380);
        var disconnectedEndpoint = new IPEndPoint(IPAddress.Loopback, 6381);
        var writableServer = _CreateServer(isConnected: true, isReplica: false);
        var replicaServer = _CreateServer(isConnected: true, isReplica: true);
        var disconnectedServer = _CreateServer(isConnected: false, isReplica: false);

        multiplexer.GetEndPoints().Returns([writableEndpoint, replicaEndpoint, disconnectedEndpoint]);
        multiplexer.GetServer(writableEndpoint).Returns(writableServer);
        multiplexer.GetServer(replicaEndpoint).Returns(replicaServer);
        multiplexer.GetServer(disconnectedEndpoint).Returns(disconnectedServer);

        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // when
        await sut.LoadAsync(scripts);

        // then
        await writableServer.Received(scripts.Length).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
        await replicaServer.DidNotReceive().ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
        await disconnectedServer.DidNotReceive().ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task should_load_requested_script_definitions()
    {
        // given
        RedisScriptDefinition[] scripts =
        [
            CustomReturnOneScriptDefinition.Instance,
            CustomReturnTwoScriptDefinition.Instance,
            CustomReturnThreeScriptDefinition.Instance,
            CustomReturnFourScriptDefinition.Instance,
        ];
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // when
        await sut.LoadAsync(scripts);

        // then
        await server.Received(scripts.Length).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task should_only_load_missing_script_definitions_when_bundle_overlaps_loaded_scripts()
    {
        // given
        var script = CustomReturnOneScriptDefinition.Instance;
        var otherScript = CustomReturnTwoScriptDefinition.Instance;
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);
        await sut.LoadAsync([script]);

        // when
        await sut.LoadAsync([script, otherScript]);

        // then
        await server.Received(2).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
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
        ((int)result)
            .Should()
            .Be(1);
        await server.Received(1).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
        await db.Received(1).ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>());
    }

    [Fact]
    public async Task should_reload_script_after_reset_scripts_clears_loaded_state()
    {
        // given
        var script = CustomReturnOneScriptDefinition.Instance;
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        var db = Substitute.For<IDatabase>();
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        _ = await sut.EvaluateAsync(db, script, parameters: null);

        // when
        sut.ResetScripts();
        _ = await sut.EvaluateAsync(db, script, parameters: null);

        // then
        await server.Received(2).ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
        await db.Received(2).ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>());
    }

    [Fact]
    public async Task should_not_lose_reset_when_reset_happens_while_loading_missing_script()
    {
        // given
        var script = CustomReturnOneScriptDefinition.Instance;
        var otherScript = CustomReturnTwoScriptDefinition.Instance;
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        var db = Substitute.For<IDatabase>();
        var firstOtherScriptLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstOtherScriptLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherScriptLoadCount = 0;

        server
            .ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var source = callInfo.ArgAt<string>(0);

                if (
                    source.Contains("return 2", StringComparison.Ordinal)
                    && Interlocked.Increment(ref otherScriptLoadCount) == 1
                )
                {
                    firstOtherScriptLoadStarted.SetResult();

                    return releaseFirstOtherScriptLoad.Task.ContinueWith(
                        _ => _CreateScriptHash(source),
                        TestContext.Current.CancellationToken,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default
                    );
                }

                return Task.FromResult(_CreateScriptHash(source));
            });

        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        using var sut = new HeadlessRedisScriptsLoader(multiplexer);
        _ = await sut.EvaluateAsync(db, script, parameters: null);

        // when
        var loadOtherScriptTask = sut.EvaluateAsync(db, otherScript, parameters: null);
        await firstOtherScriptLoadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        sut.ResetScripts();
        releaseFirstOtherScriptLoad.SetResult();

        _ = await loadOtherScriptTask;
        _ = await sut.EvaluateAsync(db, script, parameters: null);

        // then
        await server
            .Received(2)
            .ScriptLoadAsync(
                Arg.Is<string>(source => source.Contains("return 1", StringComparison.Ordinal)),
                Arg.Any<CommandFlags>()
            );
        await server
            .Received(2)
            .ScriptLoadAsync(
                Arg.Is<string>(source => source.Contains("return 2", StringComparison.Ordinal)),
                Arg.Any<CommandFlags>()
            );
    }

    [Fact]
    public async Task should_not_lose_reset_when_reset_happens_while_reloading_after_previous_reset()
    {
        // given
        var script = CustomReturnOneScriptDefinition.Instance;
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        var db = Substitute.For<IDatabase>();
        var secondScriptLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondScriptLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scriptLoadCount = 0;

        server
            .ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var source = callInfo.ArgAt<string>(0);

                if (
                    source.Contains("return 1", StringComparison.Ordinal)
                    && Interlocked.Increment(ref scriptLoadCount) == 2
                )
                {
                    secondScriptLoadStarted.SetResult();

                    return releaseSecondScriptLoad.Task.ContinueWith(
                        _ => _CreateScriptHash(source),
                        TestContext.Current.CancellationToken,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default
                    );
                }

                return Task.FromResult(_CreateScriptHash(source));
            });

        db.ScriptEvaluateAsync(Arg.Any<LoadedLuaScript>(), Arg.Any<object>())
            .Returns(Task.FromResult(RedisResult.Create(1)));

        using var sut = new HeadlessRedisScriptsLoader(multiplexer);
        _ = await sut.EvaluateAsync(db, script, parameters: null);
        sut.ResetScripts();

        // when
        var reloadTask = sut.EvaluateAsync(db, script, parameters: null);
        await secondScriptLoadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        sut.ResetScripts();
        releaseSecondScriptLoad.SetResult();

        _ = await reloadTask;

        // then
        await server
            .Received(3)
            .ScriptLoadAsync(
                Arg.Is<string>(source => source.Contains("return 1", StringComparison.Ordinal)),
                Arg.Any<CommandFlags>()
            );
    }

    [Fact]
    public async Task should_reject_multiple_script_definition_instances_for_same_concrete_type()
    {
        // given
        var script = new VariantScriptDefinition("return 1");
        var sameTypeOtherScript = new VariantScriptDefinition("return 2");
        var (multiplexer, server) = _CreateMultiplexerWithServer(isConnected: true, isReplica: false);
        using var sut = new HeadlessRedisScriptsLoader(multiplexer);

        // when
        var act = () => sut.LoadAsync([script, sameTypeOtherScript]).AsTask();

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*multiple instances*");
        await server.DidNotReceive().ScriptLoadAsync(Arg.Any<string>(), Arg.Any<CommandFlags>());
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

    private static IServer _CreateServer(bool isConnected, bool isReplica)
    {
        var server = Substitute.For<IServer>();

        server.IsConnected.Returns(isConnected);
        server.IsReplica.Returns(isReplica);

        return server;
    }

    private static byte[] _CreateScriptHash(string source)
    {
        // CA5350: SHA1 is mandatory here, not a security choice — Redis identifies cached scripts by
        // the SHA1 of their source (SCRIPT LOAD / EVALSHA). This recomputes that exact digest to
        // assert the loader's hashing behavior; it is not used to protect any secret or integrity.
#pragma warning disable CA5350
        return SHA1.HashData(Encoding.UTF8.GetBytes(source));
#pragma warning restore CA5350
    }

    private sealed class CustomReturnOneScriptDefinition : RedisScriptDefinition
    {
        public static CustomReturnOneScriptDefinition Instance { get; } = new();

        private CustomReturnOneScriptDefinition()
            : base("return 1") { }
    }

    private sealed class CustomReturnTwoScriptDefinition : RedisScriptDefinition
    {
        public static CustomReturnTwoScriptDefinition Instance { get; } = new();

        private CustomReturnTwoScriptDefinition()
            : base("return 2") { }
    }

    private sealed class CustomReturnThreeScriptDefinition : RedisScriptDefinition
    {
        public static CustomReturnThreeScriptDefinition Instance { get; } = new();

        private CustomReturnThreeScriptDefinition()
            : base("return 3") { }
    }

    private sealed class CustomReturnFourScriptDefinition : RedisScriptDefinition
    {
        public static CustomReturnFourScriptDefinition Instance { get; } = new();

        private CustomReturnFourScriptDefinition()
            : base("return 4") { }
    }

    private sealed class VariantScriptDefinition(string source) : RedisScriptDefinition(source);
}
