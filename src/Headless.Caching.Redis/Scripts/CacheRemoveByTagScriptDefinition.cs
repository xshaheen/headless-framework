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
//
// Batching: the script accepts a maxMembers cap. It processes up to maxMembers entries from the tag hash per
// invocation and returns a two-element array {removed, remaining}: removed is the count of entries unlinked
// in this batch, remaining is 1 when the hash still has members and 0 when it was fully drained. The C#
// caller loops until remaining == 0, accumulating the removed count across batches. This keeps each Lua
// script invocation bounded regardless of tag cardinality while guaranteeing complete removal.

using Headless.Redis;

namespace Headless.Caching;

/// <summary>
/// Processes up to <c>maxMembers</c> members from the tag hash per call: for each membership recorded in
/// the tag hash, the entry is unlinked only when its live physical-expiry stamp equals the recorded version;
/// otherwise the stale membership is dropped. Returns a two-element array {removedCount, hasRemaining} so
/// the C# caller can loop until the hash is fully drained while keeping each script invocation bounded.
/// </summary>
internal sealed class CacheRemoveByTagScriptDefinition : RedisScriptDefinition
{
    public static CacheRemoveByTagScriptDefinition Instance { get; } = new();

    private CacheRemoveByTagScriptDefinition()
        : base(
            """
            local headerLen = tonumber(@headerLen)
            local maxMembers = tonumber(@maxMembers)
            -- Fetch one extra member beyond the batch cap to cheaply detect whether the hash has more
            -- members remaining after this batch without issuing a second HLEN round-trip.
            local entries = redis.call('hscan', @tagHash, 0, 'COUNT', maxMembers + 1)
            local cursor = entries[1]
            local fields = entries[2]
            local removed = 0
            local toDelete = {}

            for i = 1, math.min(#fields, maxMembers * 2), 2 do
              local key = fields[i]
              local recordedMs = tonumber(fields[i + 1])
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
              end

              -- Track the field for removal from the hash regardless of match outcome: matched entries are
              -- gone, stale memberships must be pruned so the hash converges toward empty.
              toDelete[#toDelete + 1] = key
            end

            -- Bulk-remove all processed fields from the tag hash in one HDEL call.
            if #toDelete > 0 then
              redis.call('hdel', @tagHash, unpack(toDelete))
            end

            -- Determine whether the hash still has remaining members. If the HSCAN cursor is non-zero the
            -- scan is not complete; if cursor is "0" we may have consumed all members in one pass. Check
            -- hlen to be certain (it is O(1)).
            local remaining = 0
            if cursor ~= '0' or redis.call('hlen', @tagHash) > 0 then
              remaining = 1
            else
              -- Hash is fully drained — remove it.
              redis.call('unlink', @tagHash)
            end

            return {removed, remaining}
            """
        ) { }
}
