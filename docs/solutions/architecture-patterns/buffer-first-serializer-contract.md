---
title: Buffer-first ISerializer contract, untrustedData default, and PooledByteBufferWriter pooling
date: 2026-06-24
category: architecture-patterns
module: Headless.Serializer
problem_type: architecture_pattern
component: service_class
severity: medium
related_components: ["caching", "blobs"]
applies_when:
  - "Implementing or modifying an ISerializer (or any IBufferWriter-backed serialize/deserialize path)"
  - "Choosing between the ReadOnlyMemory, ReadOnlySequence, and Stream overloads at a call site"
  - "Deserializing data that may originate outside the application's trust boundary"
  - "Pooling a byte buffer across a serialize and a downstream copy/encode"
tags: ["serialization", "buffer-first", "ibufferwriter", "readonlysequence", "arraypool", "messagepack-security", "zero-copy"]
---

# Buffer-first ISerializer contract, untrustedData default, and PooledByteBufferWriter pooling

## Context

`Headless.Serializer.*` defines a provider-agnostic `ISerializer` consumed on hot paths — cache reads/writes (`Headless.Caching.Redis`), blob metadata (`Headless.Blobs.Redis`), message envelopes. The original contract was `Stream`-in / `Stream`-out, which forced a `MemoryStream` + `ToArray()` double-copy on every serialize and a `MemoryStream` wrapper on every deserialize. The hottest consumer (`RedisCache`) had grown a `MemoryMarshal`/non-writable-`MemoryStream` workaround just to claw that allocation back.

An earlier attempt (`docs/plans/2026-06-16-001-feat-zero-copy-buffer-cache-plan.md`, R2/U2) tried to bolt buffer overloads onto `IBinarySerializer` as default interface methods plus a `RawBytesSerializer`. That was abandoned — `byte[]`/`string` became the cache's native wire format (stored verbatim, never routed through a serializer), so the serializer never sat on the buffer path and the DIM bridges had nothing to do. This learning captures the **follow-through decision**: redesign the `ISerializer` contract itself to be buffer-first, rather than adding a second interface.

Measured result (BenchmarkDotNet, short job; allocation deltas are the robust signal): serialize-to-bytes ~31% faster, **−18% allocations** (1080 B -> 888 B); deserialize-from-bytes ~10% faster, −4% allocations. Plus the deleted `RedisCache` per-read workaround and −1 allocation per typed cache write. *(auto memory [claude]: serialize ~31% faster, −18% allocs; 288 tests pass.)*

## Guidance

Three coupled patterns make a buffer-first serialization layer correct and fast. Treat them as a unit — each has a non-obvious failure mode.

### 1. The contract speaks buffer primitives; Stream/byte[]/string are adapters

`ISerializer` writes to `IBufferWriter<byte>` and reads from `ReadOnlyMemory<byte>` (contiguous — the common case) or `in ReadOnlySequence<byte>` (possibly multi-segment, e.g. a `PipeReader`). These are the primitives both backends expose with the fewest copies — `System.Text.Json`'s `Utf8JsonWriter`/`Utf8JsonReader`, MessagePack's native `IBufferWriter`/`ReadOnlySequence` APIs. `byte[]`, `string`, and `Stream` live in `SerializerExtensions` as adapters because they are convenient at call sites, **not** because they are the fast path. When a caller already holds a contiguous buffer (a `byte[]`, a cache value segment), it should call the `ReadOnlyMemory<byte>` overload directly to avoid the adapter hop.

Writes have only the `IBufferWriter<byte>` sink (no `ReadOnlySequence` equivalent — that asymmetry is correct); reads have both `ReadOnlyMemory` and `ReadOnlySequence`.

### 2. A pre-built Utf8JsonWriter/Reader governs its OWN options — derive them

This is the buffer-first JSON trap. `JsonSerializer.Serialize(stream, value, options)` derives the writer's formatting internally, but when you construct a `Utf8JsonWriter`/`Utf8JsonReader` yourself over a buffer, that object governs its own formatting and limits **independently** of the `JsonSerializerOptions` you pass to `JsonSerializer.Serialize/Deserialize`. If you do not copy the relevant settings across, indentation/escaping and the configured depth limit are silently dropped on the buffer path, and the sequence path silently rejects (or accepts) payloads the span path does not.

Two further parity requirements:
- The contiguous span overload (`JsonSerializer.Deserialize(ReadOnlySpan<byte>, options)`) **rejects trailing non-whitespace** after the top-level value natively. The manual `Utf8JsonReader(sequence)` path does not — it reads one value and stops. You must replicate the rejection (`reader.Read()` after the value must return false) so a corrupt `"{...}<garbage>"` payload cannot deserialize silently on one path but throw on another.
- `MaxDepth = 0` maps to the default (64) on both reader and writer options — verify both paths agree.

### 3. PooledByteBufferWriter: copy out synchronously, before the rental returns

`PooledByteBufferWriter` is an `ArrayPool<byte>`-backed `IBufferWriter<byte>`. `WrittenSpan`/`WrittenMemory` are valid **only until the next write or `Dispose`** — `Dispose` returns the rented array to the pool. Every consumer must copy the written bytes out **synchronously** inside the `using` scope. The contract is safe today because consumers like `RedisCacheEntryFrame.Encode(ReadOnlySequence<byte>)` do `payload.CopyTo(freshArray)` synchronously and return a non-pooled `byte[]` before the scope exits. Any future async or deferred-read variant of a consumer would silently read freed pool memory — that is the invariant to protect.

Two contract points are hardened on the now-public surface, both documented in the type's public XML `<remarks>` (PR #534 first shipped them as deliberate hot-path tradeoffs; the review then took the safety side and the hardening landed):
- **`Advance` validates `count`** against the granted span (the unsigned check matching `ArrayBufferWriter<T>`) and throws `InvalidOperationException` on over-advance, surfacing misuse at the offending call rather than later at `WrittenSpan`/`WrittenMemory`. The single predicted-not-taken branch is cheap insurance once the type is `[PublicAPI]`.
- **The written span is zeroed before the rental returns to the pool** (on `Dispose` and on growth — and in the `SerializerExtensions` string transcode path). Serialized payloads (tokens, PII) do not linger in the rented array for the next renter to over-read, matching the repo's `TusAzureStore` (`Return(buffer, clearArray: true)`) convention and `System.Text.Json`'s own pooled writer. Callers no longer clear `WrittenSpan` manually; clearing only the written prefix keeps the cost off the unwritten tail.

### 4. MessagePack untrustedData: safe default that can never relax an explicit choice

`new MessagePackBinarySerializer()` defaults to `MessagePackSecurity.UntrustedData`, so the public serializer is safe for cross-service caches, external message producers, and other payloads outside the current process trust boundary. For trusted in-process payloads where the MessagePack-CSharp fast path is intentional, construct `new MessagePackBinarySerializer(untrustedData: false)` or supply explicit `MessagePackSerializerOptions` with the desired `Security`.

The switch configures **only the default (no-options) path**. When you supply your own `MessagePackSerializerOptions`, the serializer uses them verbatim and `untrustedData` is ignored — so the flag can never override or relax a `Security` level you set explicitly. Set `Security` on your options when you supply them.

## Why This Matters

- **The options-parity trap (pattern 2) is silent.** Nothing fails loudly when `WriteIndented`, `Encoder`, `MaxDepth`, `AllowTrailingCommas`, or `ReadCommentHandling` are dropped on the buffer path — output just differs from the Stream path, and a depth-limit DoS defense silently disappears. The only protection is the explicit `_WriterOptionsFor` / `_ReaderOptionsFor` mapping plus tests that assert each option survives.
- **The pooled-buffer lifetime invariant (pattern 3) is a use-after-free in waiting.** It is correct today only because every consumer copies synchronously. A reviewer must re-verify this whenever a new consumer wraps `WrittenMemory`, and especially if any `Encode`/consumer ever becomes async.
- **The untrustedData default (pattern 4) is a deliberate trust-boundary decision.** A distributed cache that other services or an attacker can write to is an untrusted source; `TrustedData` deserialization there is exploitable. The framework default is `UntrustedData`; only opt out for payloads inside a clearly owned trust boundary.
- **Buffer-first is the idiomatic .NET shape.** It matches `System.Text.Json`, MessagePack-CSharp, and `System.IO.Pipelines`. A custom `ISerializer` that bridges back to `Stream` internally reintroduces the copies the contract exists to avoid.

## When to Apply

- Implementing a new `ISerializer` (you must implement all six methods: `Serialize<T>`/`Serialize` over `IBufferWriter<byte>`; `Deserialize<T>`/`Deserialize` over both `ReadOnlyMemory<byte>` and `in ReadOnlySequence<byte>`).
- Building any `IBufferWriter<byte>`-backed pooling type, or consuming one's `WrittenSpan`/`WrittenMemory`.
- Constructing a `Utf8JsonWriter`/`Utf8JsonReader` (or any reader/writer with its own options object) over a caller-supplied buffer.
- Wiring a MessagePack serializer for a cache, blob store, or message pipeline whose payloads may cross a trust boundary.

## Examples

**Pattern 2 — derive writer/reader options from the serializer options:**

```csharp
public void Serialize<T>(T value, IBufferWriter<byte> output)
{
    var options = _optionsProvider.GetSerializeOptions();
    using var writer = new Utf8JsonWriter(output, _WriterOptionsFor(options)); // NOT defaults
    JsonSerializer.Serialize(writer, value, options);
}

private static JsonWriterOptions _WriterOptionsFor(JsonSerializerOptions o) => new()
{
    Encoder = o.Encoder, Indented = o.WriteIndented, IndentCharacter = o.IndentCharacter,
    IndentSize = o.IndentSize, NewLine = o.NewLine, MaxDepth = o.MaxDepth,
};

public T? Deserialize<T>(in ReadOnlySequence<byte> data)
{
    var options = _optionsProvider.GetDeserializeOptions();
    var reader = new Utf8JsonReader(data, _ReaderOptionsFor(options)); // AllowTrailingCommas, CommentHandling, MaxDepth
    var result = JsonSerializer.Deserialize<T>(ref reader, options);
    if (reader.Read()) // mirror the span overload's trailing-content rejection
        throw new JsonException("The input contains trailing content after the top-level JSON value.");
    return result;
}
```

**Pattern 3 — copy out synchronously, inside the using scope:**

```csharp
// RedisCache write path: serialize into a pooled buffer, then Encode copies once into the frame.
using var buffer = new PooledByteBufferWriter();
serializer.Serialize(value, buffer);
// Encode does payload.CopyTo(freshArray) synchronously and returns a non-pooled byte[]
// BEFORE this using scope disposes the rental — never retain WrittenMemory past here.
return RedisCacheEntryFrame.Encode(new ReadOnlySequence<byte>(buffer.WrittenMemory), isNull: false, /* ... */);
```

**Pattern 4 — untrustedData default vs. supplied options own security:**

```csharp
// Default safe path for untrusted payloads.
services.AddSingleton<IBinarySerializer, MessagePackBinarySerializer>();

// Trusted payload fast path only when the trust boundary is explicit:
services.AddSingleton<IBinarySerializer>(new MessagePackBinarySerializer(untrustedData: false));

// Supplied options own security; untrustedData is ignored here and cannot relax your choice:
var options = MessagePackSerializerOptions.Standard
    .WithResolver(ContractlessStandardResolver.Instance)
    .WithSecurity(MessagePackSecurity.UntrustedData);
services.AddSingleton<IBinarySerializer>(new MessagePackBinarySerializer(options));
```

## Related

- `docs/plans/2026-06-16-001-feat-zero-copy-buffer-cache-plan.md` — the zero-copy cache plan whose R2/U2 DIM-overload approach was abandoned; this contract redesign is the chosen follow-through ("do not re-implement the DIM overloads").
- `docs/llms/serialization.md` and the `Headless.Serializer.*` package READMEs — consumer-facing docs for the contract, overload selection, and the `untrustedData` decision.
- `docs/llms/caching.md` — the `IBufferCache` / `RedisCacheEntryFrame` design; `byte[]` is the cache's native wire format (stored verbatim, never serialized), architecturally independent of this serializer buffer path.
- `docs/solutions/architecture-patterns/unified-provider-setup-builder-pattern.md` — governs the open follow-up to add `Add{Feature}` DI registration extensions for the serializer packages (single-backend -> plain `Add{Feature}` on `IServiceCollection`, not the multi-provider builder shape).
- PR #534 (`perf(serializer)!: buffer-first ISerializer`).
