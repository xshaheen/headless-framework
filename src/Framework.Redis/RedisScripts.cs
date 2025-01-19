// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Redis;

public static class RedisScripts
{
    public const string ReplaceIfEqual = """
        local currentVal = redis.call('get', @key)
        if (currentVal == false or currentVal == @expected) then
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

    public const string IncrementWithExpire = """
        if math.modf(@value) == 0 then
          local v = redis.call('incrby', @key, @value)
          if (@expires ~= nil and @expires ~= '') then
            redis.call('pexpire', @key, math.ceil(@expires))
          end
          return tonumber(v)
        else
          local v = redis.call('incrbyfloat', @key, @value)
          if (@expires ~= nil and @expires ~= '') then
            redis.call('pexpire', @key, math.ceil(@expires))
          end
          return tonumber(v)
        end
        """;
}
