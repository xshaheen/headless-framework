// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Reserved tag-index namespace: "{KeyPrefix}__cache_tag__:{tag}". Each tag is a Redis HASH whose fields are
// the full (prefixed) cache keys carrying the tag and whose values are the entry's PhysicalExpiresAt as an
// invariant Unix-millisecond integer string — the entry "version" that RemoveByTag pins memberships to.
// Consumers must not store cache entries under keys starting with "{KeyPrefix}__cache_tag__:".
//
// NOTE: the script constructs the tag hash key names from the tag prefix, so the touched keys do not hash to
// a single slot. Tag invalidation is therefore NOT supported on Redis Cluster (same limitation as comparable
// tag-index designs); standalone/replicated deployments are unaffected.

using Headless.Redis;

namespace Headless.Caching;

/// <summary>
/// Atomically writes a tagged framed cache entry and reconciles the reverse tag index: SET the framed value
/// with the key TTL, HSET each current tag's hash with the entry's physical-expiry version, extend each tag
/// hash TTL with greater-than semantics, and HDEL the memberships for tags this write drops.
/// </summary>
internal sealed class CacheTaggedSetScriptDefinition : RedisScriptDefinition
{
    public static CacheTaggedSetScriptDefinition Instance { get; } = new();

    private CacheTaggedSetScriptDefinition()
        : base(
            """
            local keyTtlMs = tonumber(@keyTtlMs)
            local tagTtlMs = tonumber(@tagTtlMs)

            if @expectedValue ~= nil and string.len(@expectedValue) > 0 then
              local current = redis.call('get', @key)
              if current ~= @expectedValue then
                return 0
              end
            end

            redis.call('set', @key, @value, 'PX', keyTtlMs)

            -- Parse a tag blob: u16le count, then per tag a u16le UTF-8 byte length + the tag bytes.
            local function readTags(blob)
              local tags = {}
              if blob == nil or string.len(blob) < 2 then
                return tags
              end
              local count = string.byte(blob, 1) + string.byte(blob, 2) * 256
              local offset = 3
              for i = 1, count do
                local len = string.byte(blob, offset) + string.byte(blob, offset + 1) * 256
                tags[i] = string.sub(blob, offset + 2, offset + 1 + len)
                offset = offset + 2 + len
              end
              return tags
            end

            for _, tag in ipairs(readTags(@tags)) do
              local tagKey = @tagPrefix .. tag
              redis.call('hset', tagKey, @key, @physicalMs)
              -- EXPIRE ... GT emulation (atomic within this script, no Redis 7 dependency): only ever extend.
              -- PTTL == -1 (no expiry) can only mean the HSET above just created the hash — we always stamp
              -- TTLs — so stamping it is the safe direction and prevents an unexpiring index hash.
              local current = redis.call('pttl', tagKey)
              if current == -1 or current < tagTtlMs then
                redis.call('pexpire', tagKey, tagTtlMs)
              end
            end

            for _, tag in ipairs(readTags(@removedTags)) do
              redis.call('hdel', @tagPrefix .. tag, @key)
            end

            return 1
            """
        ) { }
}
