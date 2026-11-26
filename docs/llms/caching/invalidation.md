---
domain: Caching
packages:
---

# Invalidation

> Part of [Caching](../caching.md). How `RemoveAsync`, `ExpireAsync`, `RemoveByTagAsync`, `ClearAsync`, and `FlushAsync` differ, what each keeps, and how fast other instances see them.

## Pick the call by what must survive

| Call | Scope | Fail-safe reserve | Cost and shape |
| --- | --- | --- | --- |
| `RemoveAsync(key)` | One key | Deleted | Physical delete. Returns `true` when the key existed. |
| `ExpireAsync(key)` | One key | Kept | Logical expiry: normal reads miss at once. Returns `true` when the key existed. |
| `RemoveByTagAsync(tag)` | Every entry carrying the tag | Kept | O(1) logical: writes one per-tag marker. Returns `ValueTask` with no count. |
| `ClearAsync()` | Whole cache | Kept | O(1) logical: bumps one clear marker. |
| `FlushAsync()` | Whole cache | Deleted | In process: physical wipe. On a distributed tier (Redis): one logical remove marker, cluster-safe, with no `FLUSHDB`; each entry's TTL reclaims the memory. |

- Use `ExpireAsync` to force a refresh while keeping the stale fallback: a later `GetOrAddAsync` whose factory fails, with `IsFailSafeEnabled`, still serves the old value. Use `RemoveAsync` when the value must be gone for good: a later failing factory has nothing to fall back to.
- For an entry with no reserve (a plain `TimeSpan?` write, or fail-safe off), `ExpireAsync` equals `RemoveAsync`.
- A sliding entry's extra physical lifetime is its absolute cap, not a reserve, so `ExpireAsync` removes a sliding entry. On Redis it also removes a key written without the Headless envelope.
- Use `ClearAsync` when fail-safe coverage must outlive the clear, and `FlushAsync` to drop reserves too.

## Tags

Write tags with `CacheEntryOptions.Tags` on `GetOrAddAsync` or `UpsertEntryAsync`. The plain `UpsertAsync` takes no options and writes no tags.

```csharp
await cache.UpsertEntryAsync(
    "product:42",
    product,
    new CacheEntryOptions { Duration = TimeSpan.FromMinutes(30), Tags = ["products"] },
    ct
);

await cache.RemoveByTagAsync("products", ct); // O(1) logical invalidation: bumps the "products" marker

// Logical whole-cache clear (reserves preserved); FlushAsync drops reserves instead.
await cache.ClearAsync(ct);
```

- Each tag must be non-empty, and both the tag count and each tag's UTF-8 byte length must fit in an unsigned 16-bit value. A violation throws `ArgumentException` before anything is written.
- On a factory read, tags passed in the call replace the tags on the existing entry. `Tags = null` keeps the existing tags.
- `RemoveByTagAsync` and `ClearAsync` work on Redis Cluster: one marker key per tag, one clear key, and no cross-slot enumeration.

Until a standalone in-memory cache stores its first tagged entry, it keeps per-tag history only while a factory or tagged write is in flight. After a tagged entry has been stored, maintenance prunes markers once the longest observed tagged lifetime makes them safe to discard. A manual tagged write through `IFactoryCacheStore.SetEntryAsync` returns `false` if its birth timestamp predates discarded history.

Hybrid retains a seeded tag marker while a local entry or in-flight publication can depend on it and until L2 confirms its durable timestamp. An unavailable L2 or a provider without durable confirmation leaves the marker pinned. An entry imported from Redis compares tag timestamps at Redis's whole-millisecond precision. New local entries and factory results retain full timestamp precision.

## What a logical invalidation does

Every entry records its birth time. A read treats the entry as invalidated when any marker that applies to it (the clear marker, or a marker of one of its tags) is newer than that birth time.

- **Direct reads** (`GetAsync`, `ExistsAsync`, and the rest) miss an invalidated entry.
- **`GetOrAddAsync`** runs the factory, and keeps the invalidated entry as a fail-safe reserve: a failing factory can still serve it stale.
- **A key written after the marker is not invalidated.** Its birth time is newer than the marker.
- **A factory value is born when the factory starts, not when it returns.** So a `RemoveByTagAsync` or `ClearAsync` issued while a factory is still reading its source invalidates the value that factory writes. The value's expiry still counts from the write.
- **Entries stay physically present.** Counts, key listing, and prefix reads still see invalidated keys until they expire or are evicted. Each entry's physical TTL also bounds staleness if a marker is ever lost.
- **Conditional writes agree with reads.** `TryInsertAsync` overwrites a logically dead entry (expired inside its reserve, or invalidated by a tag, clear, or flush). `TryReplaceAsync`, `TryReplaceIfEqualAsync`, and `RemoveIfEqualAsync` return `false` for it and leave it in place.
- **Redis native counters and sets use the flush generation.** `FlushAsync` advances it even when the clock has not moved. Their data keys stay in place. A later read misses an old generation, and a later mutation starts from an empty counter or set.

## How fast other instances see it

- **The instance that invalidates** sees it on its next read.
- **Pure Redis, no hybrid.** Each instance caches the markers for framed entries for `RedisCacheOptions.TagMarkerRefreshWindow` (default 2 seconds). Native counter and set operations check the current flush generation on the primary.
- **Hybrid with a backplane.** A receiving instance updates both its L1 and its L2 marker view at once from the notification's timestamp, so the window does not apply. It still bounds recovery after a missed backplane message.
- **The marker timestamp travels with the message.** A receiver uses the sender's timestamp, not its own clock, so a receiver whose clock lags cannot record a marker older than an entry the invalidation should hit.

## Reserved key namespace

On Redis the invalidation markers live at `{KeyPrefix}\0__tag:{tag}`, `{KeyPrefix}\0__clear`, and `{KeyPrefix}\0__remove`. Native generation metadata uses keys starting with `\0__native:` and a hash tag for the data key's Redis Cluster slot. These reserved keys contain a NUL byte (U+0000), which is disallowed in cache keys.
