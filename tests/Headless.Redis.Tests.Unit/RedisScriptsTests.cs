// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisScriptsTests
{
    [Fact]
    public void ReplaceIfEqual_script_should_be_valid_lua()
    {
        // when
        var act = () => LuaScript.Prepare(RedisScripts.ReplaceIfEqual);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveIfEqual_script_should_be_valid_lua()
    {
        // when
        var act = () => LuaScript.Prepare(RedisScripts.RemoveIfEqual);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void SetIfHigher_script_should_be_valid_lua()
    {
        // when
        var act = () => LuaScript.Prepare(RedisScripts.SetIfHigher);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void SetIfLower_script_should_be_valid_lua()
    {
        // when
        var act = () => LuaScript.Prepare(RedisScripts.SetIfLower);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void IncrementWithExpire_script_should_be_valid_lua()
    {
        // when
        var act = () => LuaScript.Prepare(RedisScripts.IncrementWithExpire);

        // then
        act.Should().NotThrow();
    }
}
