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

## What a logical invalidation does

Every entry records its birth time. A read treats the entry as invalidated when any marker that applies to it (the clear marker, or a marker of one of its tags) is newer than that birth time.

- **Direct reads** (`GetAsync`, `ExistsAsync`, and the rest) miss an invalidated entry.
- **`GetOrAddAsync`** runs the factory, and keeps the invalidated entry as a fail-safe reserve: a failing factory can still serve it stale.
- **A key written after the marker is not invalidated.** Its birth time is newer than the marker.
- **A factory value is born when the factory starts, not when it returns.** So a `RemoveByTagAsync` or `ClearAsync` issued while a factory is still reading its source invalidates the value that factory writes. The value's expiry still counts from the write.
- **Entries stay physically present.** Counts, key listing, and prefix reads still see invalidated keys until they expire or are evicted. Each entry's physical TTL also bounds staleness if a marker is ever lost.
- **Conditional writes agree with reads.** `TryInsertAsync` overwrites a logically dead entry (expired inside its reserve, or invalidated by a tag, clear, or flush). `TryReplaceAsync`, `TryReplaceIfEqualAsync`, and `RemoveIfEqualAsync` return `false` for it and leave it in place.

## How fast other instances see it

- **The instance that invalidates** sees it on its next read.
- **Pure Redis, no hybrid.** Each instance caches the markers it read for `RedisCacheOptions.TagMarkerRefreshWindow` (default 2 seconds). Another instance sees a new marker only after its window elapses.
- **Hybrid with a backplane.** A receiving instance updates both its L1 and its L2 marker view at once from the notification's timestamp, so the window does not apply. It still bounds recovery after a missed backplane message.
- **The marker timestamp travels with the message.** A receiver uses the sender's timestamp, not its own clock, so a receiver whose clock lags cannot record a marker older than an entry the invalidation should hit.

## Reserved key namespace

On Redis the markers live at `{KeyPrefix}\0__tag:{tag}` and `{KeyPrefix}\0__clear`, behind a NUL byte (U+0000). Ordinary keys cannot collide with them. Do not put a NUL byte in your own cache keys.
