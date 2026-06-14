// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Operates on the reserved tag-index namespace "{KeyPrefix}__cache_tag__:{tag}" maintained by
// CacheTaggedSetScriptDefinition. Memberships are pinned to the entry version (its PhysicalExpiresAt
// Unix-millisecond stamp recorded as the hash field value), so a key that expired, was plain-SET
// overwritten, or re-created without the tag carries a DIFFERENT physical stamp and is skipped (the stale
// membership is cleaned up instead) — FusionCache-faithful RemoveByTag semantics.
//
// The header parse mirrors the CAS scripts (CacheRemoveIfEqualScriptDefinition): magic 0xFF, version 0x02,
// flags at byte 3, logical expiry as u64le Unix ms at fixed offsets 4..11 and physical expiry at 12..19
// (1-based). GETRANGE key 0 18 reads exactly the 19-byte fixed header. The hash fields reference keys outside
// the script's KEYS, so tag invalidation is NOT supported on Redis Cluster.
//
// Fail-safe reserve guard: an entry whose logical expiry has passed but whose physical expiry has not is a
// fail-safe reserve (ExpireAsync logically expires it while keeping the physical reserve so a later failing
// fail-safe factory can still serve the stale value). Such reserves are NOT unlinked and their tag membership
// is NOT pruned — they must survive RemoveByTag and stay tag-discoverable. Because reserves are retained, the
// "remaining" signal means "more PRUNABLE members exist" (not merely "hash non-empty"); a batch that examined
// members but pruned none has reached a fixed point and stops, so reserves never spin the C# loop.
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
            -- Server clock in Unix ms, used to distinguish a still-valid fail-safe reserve (logically expired
            -- but physically present) from a fully-expired entry. TIME returns {seconds, microseconds}.
            local serverTime = redis.call('time')
            local nowMs = tonumber(serverTime[1]) * 1000 + math.floor(tonumber(serverTime[2]) / 1000)
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
              local isReserve = false

              if recordedMs ~= nil
                and string.len(header) >= headerLen
                and string.byte(header, 1) == 255
                and string.byte(header, 2) == 3
              then
                local flags = string.byte(header, 3)

                -- HasPhysicalExpiresAtFlag = 0x04
                if math.floor(flags / 4) % 2 == 1 then
                  -- u64le physical expiry at fixed bytes 12..19 (1-based); Unix ms fits Lua's 53-bit doubles.
                  -- Use the literal field end (19), not headerLen: v3 appends an 8-byte CreatedAt slot after
                  -- physical, so headerLen (27) would over-read CreatedAt bytes into the physical value.
                  local physicalMs = 0
                  for b = 19, 12, -1 do
                    physicalMs = physicalMs * 256 + string.byte(header, b)
                  end

                  if physicalMs == recordedMs then
                    matches = true

                    -- A fail-safe reserve is a logically-expired-but-physically-present entry: ExpireAsync
                    -- pulls the logical stamp to now while keeping the physical reserve so a later failing
                    -- fail-safe factory can still serve the stale value. The reserve must survive RemoveByTag
                    -- (otherwise the parachute is destroyed) AND stay tag-discoverable, so skip BOTH the unlink
                    -- and the membership prune for it. HasLogicalExpiresAtFlag = 0x02.
                    if math.floor(flags / 2) % 2 == 1 then
                      -- u64le logical expiry at bytes 4..11 (1-based).
                      local logicalMs = 0
                      for b = 11, 4, -1 do
                        logicalMs = logicalMs * 256 + string.byte(header, b)
                      end

                      if logicalMs <= nowMs and physicalMs > nowMs then
                        isReserve = true
                      end
                    end
                  end
                end
              end

              if isReserve then
                -- Leave the entry and its tag membership intact: it is a live reserve fail-safe still needs.
              else
                if matches then
                  redis.call('unlink', key)
                  removed = removed + 1
                end

                -- Prune the processed field from the hash: matched non-reserve entries are gone, and stale
                -- memberships (version mismatch / missing key) must be pruned so the hash converges toward empty.
                toDelete[#toDelete + 1] = key
              end
            end

            -- Bulk-remove all processed fields from the tag hash in one HDEL call.
            if #toDelete > 0 then
              redis.call('hdel', @tagHash, unpack(toDelete))
            end

            -- Determine whether the hash still has prunable members. Reserves are deliberately retained, so a
            -- batch that examined members but pruned none (everything left is a reserve) has reached a fixed
            -- point: the C# loop must stop instead of re-scanning the same reserves until the budget is spent.
            -- "remaining" therefore signals "more prunable work", not merely "hash non-empty".
            local remaining = 0
            local hashLen = redis.call('hlen', @tagHash)

            if hashLen == 0 then
              -- Hash is fully drained — remove it.
              redis.call('unlink', @tagHash)
            elseif #toDelete > 0 and (cursor ~= '0' or hashLen > 0) then
              -- Made progress this batch and members remain — keep looping to process them.
              remaining = 1
            end
            -- else: members remain but none were prunable this batch (all reserves) → stop; the hash is left in
            -- place so the reserves stay tag-discoverable.

            return {removed, remaining}
            """
        ) { }
}
