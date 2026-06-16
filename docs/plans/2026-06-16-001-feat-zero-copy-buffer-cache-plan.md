# Zero-intermediate-copy buffer cache path

Plan for a byte-streaming fast path through the caching stack so the ASP.NET Core output-cache
adapter (and the BCL `IDistributedCache` adapter) can read/write payloads without the intermediate
`byte[]` materializations they pay today.

> **Resolution note (implemented differently from R2/U2):** the `IBinarySerializer` DIM buffer
> overloads + `RawBytesSerializer` override described in R2 and U2 were **not** shipped. During
> implementation `byte[]` (and `string`) were made the cache's *native wire format* — stored verbatim,
> never routed through a serializer — so the serializer never sits on the buffer path and the DIM
> bridges had nothing to do. `RawBytesSerializer` was deleted instead of promoted. The buffer
> zero-copy goal is fully met through the provider codecs (`IBufferCache` on Redis/InMemory/Hybrid);
> do not re-implement the DIM overloads. See `docs/llms/serialization.md` / `docs/llms/caching.md`.

## Context

`HeadlessOutputCacheStore` implements `IOutputCacheBufferStore`, whose purpose is to let a store
stream the response body without allocating a `byte[]`. Today the adapter defeats that: the buffer
`SetAsync(ReadOnlySequence<byte>)` does `value.ToArray()`, and `TryGetAsync(PipeWriter)` reads a
`byte[]` from `ICache` then copies it into the pipe. The copies are structural — `ICache` and
`IBinarySerializer` only speak `byte[]`-over-`Stream`.

**Floor (honest target).** A distributed cache must put the payload on the wire and read it back —
one unavoidable I/O copy per side. The removable waste is the 1–2 intermediate `byte[]` copies.

| Path | Today | Target |
| --- | --- | --- |
| `TryGetAsync` (read) | 3 payload copies (wire→byte[] → MemoryStream→byte[] → pipe) | 1 (wire slice → `PipeWriter`) |
| `SetAsync` (write) | 2–3 (ToArray → serialize → frame/network) | 1 (sequence → framed network buffer) |

`PipeWriter : IBufferWriter<byte>` is the lever: the buffer interface was built so a store can write
straight into a caller-supplied buffer writer.

## Locked design decisions

- **KTD1 — Capability interface `IBufferCache`, not widening `ICache`.** Byte-oriented providers
  (Redis, InMemory, Hybrid) implement it alongside `ICache`; the adapter feature-detects
  (`cache is IBufferCache`) and falls back to the `byte[]` path otherwise. Keeps the generic
  object-cache surface clean.
- **KTD2 — Providers in scope: Redis + InMemory + Hybrid.** Default-impl on the interface keeps any
  other/future provider working via the `byte[]` fallback.
- **KTD3 — `IBinarySerializer` gains buffer overloads as default interface methods.**
  `Serialize<T>(T, IBufferWriter<byte>)` / `Deserialize<T>(ReadOnlySequence<byte>)`, defaulting to a
  bridge over the existing `Stream` API; `RawBytesSerializer` (and other hot serializers) override.
  Existing serializers keep compiling; perf-critical ones opt into the fast path.
- **KTD4 — `UpsertRawAsync` reuses the stamping/framing pipeline.** The raw payload must be stamped
  exactly like `UpsertEntryAsync` (`CreatedAt`, tags, fail-safe, sliding) so tag invalidation and
  fail-safe still work for output-cache entries — the only difference is the payload arrives as bytes
  rather than a serialized `T`. This is the meatiest provider change (Redis `RedisCacheEntryFrame`).

## Requirements

**Contract**
- R1. New `IBufferCache` capability interface in `Headless.Caching.Abstractions`:
  `ValueTask<bool> TryGetToAsync(string key, IBufferWriter<byte> destination, CancellationToken)` and
  `ValueTask<bool> UpsertRawAsync(string key, ReadOnlySequence<byte> value, CacheEntryOptions, CancellationToken)`.
- R2. `IBinarySerializer` buffer overloads (`Serialize<T>(T, IBufferWriter<byte>)`,
  `Deserialize<T>(ReadOnlySequence<byte>)`) with DIM bridges; `RawBytesSerializer` overrides both for
  true zero-transform passthrough.
- R3. An adapter-facing helper so a consumer can do "raw if available, else byte[] fallback" in one
  call (avoid every consumer re-implementing the feature-detect).

**Provider behavior (each: Redis, InMemory, Hybrid)**
- R4. `TryGetToAsync` writes the decoded payload slice into the supplied `IBufferWriter<byte>` with no
  standalone `byte[]`, returns false on miss/expiry (nothing written).
- R5. `UpsertRawAsync` stamps and persists the payload with identical semantics to `UpsertEntryAsync`
  (CreatedAt, tags, expiration, fail-safe, sliding) — verified by parity tests against the generic path.
- R6. The pooled-buffer-before-yield invariant holds inside the provider: the `ReadOnlySequence<byte>`
  is consumed synchronously before the first await.
- R7. Byte fidelity: a value written raw and read back (raw or generic) is byte-identical, and vice
  versa (cross-path round-trip).

**Adapter**
- R8. `HeadlessOutputCacheStore` buffer members use `IBufferCache` when the named cache implements it,
  else the current `byte[]` path. No behavior change for tag eviction / TTL / fidelity.

**Quality**
- R9. Unit + integration coverage per provider; cross-path round-trip; fallback path; allocation
  assertion (no intermediate `byte[]` on the fast path). Docs synced.

## Implementation Units

- **U1. `IBufferCache` abstraction + adapter helper** (`Headless.Caching.Abstractions`). Interface +
  a `BufferCacheExtensions` helper for feature-detect-or-fallback. (R1, R3)
- **U2. Serializer buffer overloads** (`Headless.Serializer.Abstractions` + `RawBytesSerializer` in
  `Headless.Caching.Core`). DIM bridges + override. (R2)
- **U3. Redis provider** (`Headless.Caching.Redis`). `RedisCache : IBufferCache`; extend
  `RedisCacheEntryFrame` to encode from a `ReadOnlySequence<byte>` payload and decode to a payload
  slice written into an `IBufferWriter<byte>`, preserving the stamp/tag frame. Hardest unit. (R4–R7)
- **U4. InMemory provider** (`Headless.Caching.InMemory`). Store framed bytes; slice to the writer on
  read; copy the sequence on write. (R4–R7)
- **U5. Hybrid provider** (`Headless.Caching.Hybrid`). L1 slice on hit; L2 raw passthrough + L1 seed;
  raw upsert stamps both tiers + backplane. (R4–R7)
- **U6. Output-cache adapter** (`Headless.Caching.OutputCache`). Rewire buffer members to the helper. (R8)
- **U7. BCL adapter** (`Headless.Caching.Bcl`) — optional follow-up; same helper for its buffer paths.
- **U8. Tests** — per-provider raw round-trip, cross-path fidelity, stamping/tag parity, fallback,
  allocation assertions. (R9)
- **U9. Docs** — `docs/llms/caching.md` + provider READMEs + output-cache README. (R9)

## Sequencing / checkpoints

1. **Foundation:** U1 + U2 compile (abstraction + serializer). **Checkpoint.**
2. **Providers:** U3 (Redis) → U4 (InMemory) → U5 (Hybrid), each with its tests green before the next.
3. **Adapter + docs:** U6, U8 adapter tests, U9. U7 (BCL) optional.

## Open questions

- Does `RedisCacheEntryFrame` decoding currently expose the payload as a slice of the received buffer,
  or does it already copy? Determines whether U3 read hits the 1-copy floor or needs frame-decode rework.
- Hybrid L1 read: can it expose the stored payload as `ReadOnlyMemory<byte>` without handing out a
  mutable shared array? May require storing an immutable wrapper.
