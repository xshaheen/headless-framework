// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Redis;

public static class RedisScripts
{
    public const string ReplaceIfEqual = """
        local currentVal = redis.call('get', @key)
        if (currentVal ~= false and currentVal == @expected) then
          if (@expires ~= nil and @expires ~= '') then
            return redis.call('set', @key, @value, 'PX', @expires) and 1 or 0
          else
            return redis.call('set', @key, @value) and 1 or 0
          end
        else
          return -1
        end
        """;

    public const string RemoveIfEqual = """
        if redis.call('get', @key) == @expected then
          return redis.call('del', @key)
        else
          return 0
        end
        """;

    public const string SetIfHigher = """
        local c = tonumber(redis.call('get', @key))
        if c then
          if tonumber(@value) > c then
            redis.call('set', @key, @value)
            if (@expires ~= nil and @expires ~= '') then
              redis.call('pexpire', @key, math.ceil(@expires))
            end
          end
        else
          redis.call('set', @key, @value)
          if (@expires ~= nil and @expires ~= '') then
            redis.call('pexpire', @key, math.ceil(@expires))
          end
        end
        return redis.call('get', @key)
        """;

    public const string SetIfLower = """
        local c = tonumber(redis.call('get', @key))
        if c then
          if tonumber(@value) < c then
            redis.call('set', @key, @value)
            if (@expires ~= nil and @expires ~= '') then
              redis.call('pexpire', @key, math.ceil(@expires))
            end
          end
        else
          redis.call('set', @key, @value)
          if (@expires ~= nil and @expires ~= '') then
            redis.call('pexpire', @key, math.ceil(@expires))
          end
        end
        return redis.call('get', @key)
        """;

    public const string IncrementWithExpire = """
        if math.modf(@value) == 0 then
          redis.call('incrby', @key, @value)
        else
          redis.call('incrbyfloat', @key, @value)
        end
        if (@expires ~= nil and @expires ~= '') then
          redis.call('pexpire', @key, math.ceil(@expires))
        end
        return redis.call('get', @key)
        """;
}
