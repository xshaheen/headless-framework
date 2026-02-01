// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Redis;

/// <summary>
/// Lua scripts for atomic Redis operations.
/// </summary>
/// <remarks>
/// <para>
/// All scripts return string values to preserve floating-point precision (Redis EVAL converts
/// Lua numbers to integers, truncating decimals).
/// </para>
/// <para>
/// Expiration parameters accept milliseconds as integer or empty string for no expiration.
/// </para>
/// </remarks>
public static class RedisScripts
{
    /// <summary>
    /// Atomically replaces a value only if it matches the expected value.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@value</c> - The new value to set</item>
    ///   <item><c>@expected</c> - The expected current value (empty string = key should not exist)</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b>
    /// <list type="bullet">
    ///   <item><c>1</c> - Value was replaced successfully</item>
    ///   <item><c>0</c> - SET command failed (rare)</item>
    ///   <item><c>-1</c> - Current value doesn't match expected</item>
    /// </list>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Compare-and-swap (CAS) operations</item>
    ///   <item>Insert-if-not-exists when expected is empty string</item>
    ///   <item>Distributed lock renewal</item>
    /// </list>
    /// </remarks>
    public const string ReplaceIfEqual = """
        local currentVal = redis.call('get', @key)
        local expected = @expected
        if expected == '' then expected = false end
        if currentVal == expected then
          if (@expires ~= nil and @expires ~= '') then
            return redis.call('set', @key, @value, 'PX', @expires) and 1 or 0
          else
            return redis.call('set', @key, @value) and 1 or 0
          end
        else
          return -1
        end
        """;

    /// <summary>
    /// Atomically removes a key only if its value matches the expected value.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@expected</c> - The expected current value</item>
    /// </list>
    /// <b>Returns:</b>
    /// <list type="bullet">
    ///   <item><c>1</c> - Key was deleted successfully</item>
    ///   <item><c>0</c> - Key doesn't exist or value doesn't match</item>
    /// </list>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Safe distributed lock release (only release if you own the lock)</item>
    ///   <item>Conditional cache invalidation</item>
    /// </list>
    /// </remarks>
    public const string RemoveIfEqual = """
        if redis.call('get', @key) == @expected then
          return redis.call('del', @key)
        else
          return 0
        end
        """;

    /// <summary>
    /// Sets a value only if it's higher than the current value. Creates the key if it doesn't exist.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@value</c> - The new value (as string to preserve precision)</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b> The final stored value as string (new value if updated, existing value if not).
    /// <list type="bullet">
    ///   <item>If key exists and new value &gt; current: returns new value</item>
    ///   <item>If key exists and new value ≤ current: returns current value (unchanged)</item>
    ///   <item>If key doesn't exist: creates key, returns new value</item>
    /// </list>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Tracking maximum values (high scores, peak metrics)</item>
    ///   <item>Monotonic counters that should never decrease</item>
    /// </list>
    /// </remarks>
    public const string SetIfHigher = """
        local c = tonumber(redis.call('get', @key))
        local v = tonumber(@value)
        if c then
          if v > c then
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

    /// <summary>
    /// Sets a value only if it's lower than the current value. Creates the key if it doesn't exist.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@value</c> - The new value (as string to preserve precision)</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b> The final stored value as string (new value if updated, existing value if not).
    /// <list type="bullet">
    ///   <item>If key exists and new value &lt; current: returns new value</item>
    ///   <item>If key exists and new value ≥ current: returns current value (unchanged)</item>
    ///   <item>If key doesn't exist: creates key, returns new value</item>
    /// </list>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Tracking minimum values (lowest prices, fastest times)</item>
    ///   <item>Water-level markers that track historical lows</item>
    /// </list>
    /// </remarks>
    public const string SetIfLower = """
        local c = tonumber(redis.call('get', @key))
        local v = tonumber(@value)
        if c then
          if v < c then
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

    /// <summary>
    /// Atomically increments a value and sets expiration in a single operation.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@value</c> - The increment amount (positive or negative)</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b> The new value after increment as string.
    /// <para>
    /// Automatically uses INCRBY for integers and INCRBYFLOAT for decimals.
    /// Creates the key with value 0 before incrementing if it doesn't exist.
    /// </para>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Rate limiting counters with automatic expiration</item>
    ///   <item>Rolling window metrics</item>
    ///   <item>Session-scoped counters</item>
    /// </list>
    /// </remarks>
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
