// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Operates on the reserved tag-index namespace "{KeyPrefix}__cache_tag__:{tag}" maintained by
// CacheTaggedSetScriptDefinition. Memberships are pinned to the entry version (its PhysicalExpiresAt
// Unix-millisecond stamp recorded as the hash field value), so a key that expired, was plain-SET
// overwritten, or re-created without the tag carries a DIFFERENT physical stamp and is skipped (the stale
// membership is cleaned up instead) — FusionCache-faithful RemoveByTag semantics.
//
// The header parse mirrors the CAS scripts (CacheRemoveIfEqualScriptDefinition): magic 0xFF, version 0x02,
// flags at byte 3, physical expiry as u64le Unix ms at fixed offsets 12..19 (1-based). GETRANGE key 0 18
// reads exactly the 19-byte fixed header. The hash fields reference keys outside the script's KEYS, so tag
// invalidation is NOT supported on Redis Cluster.

using Headless.Redis;

namespace Headless.Caching;

/// <summary>
/// Atomically removes every cache entry that currently carries a tag: for each membership recorded in the
/// tag hash, the entry is unlinked only when its live physical-expiry stamp equals the recorded version;
/// otherwise the stale membership is dropped. The tag hash itself is unlinked at the end. Returns the number
/// of entries removed.
/// </summary>
internal sealed class CacheRemoveByTagScriptDefinition : RedisScriptDefinition
{
    public static CacheRemoveByTagScriptDefinition Instance { get; } = new();

    private CacheRemoveByTagScriptDefinition()
        : base(
            """
            local headerLen = tonumber(@headerLen)
            local entries = redis.call('hgetall', @tagHash)
            local removed = 0

            for i = 1, #entries, 2 do
              local key = entries[i]
              local recordedMs = tonumber(entries[i + 1])
              local header = redis.call('getrange', key, 0, headerLen - 1)
              local matches = false

              if recordedMs ~= nil
                and string.len(header) >= headerLen
                and string.byte(header, 1) == 255
                and string.byte(header, 2) == 2
              then
                local flags = string.byte(header, 3)

                -- HasPhysicalExpiresAtFlag = 0x04
                if math.floor(flags / 4) % 2 == 1 then
                  -- u64le physical expiry at bytes 12..19 (1-based); Unix ms fits Lua's 53-bit doubles.
                  local physicalMs = 0
                  for b = headerLen, 12, -1 do
                    physicalMs = physicalMs * 256 + string.byte(header, b)
                  end

                  if physicalMs == recordedMs then
                    matches = true
                  end
                end
              end

              if matches then
                redis.call('unlink', key)
                removed = removed + 1
              else
                redis.call('hdel', @tagHash, key)
              end
            end

            redis.call('unlink', @tagHash)

            return removed
            """
        ) { }
}
