# Headless.Serializer.MessagePack

MessagePack binary serialization implementation of `IBinarySerializer`.

## Why use this package

Provides compact binary serialization for high-throughput scenarios (cache entries, internal message envelopes) where JSON text overhead is a bottleneck. Contractless by default — no `[MessagePackObject]` or `[Key]` attributes required.

## Install

```bash
dotnet add package Headless.Serializer.MessagePack
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Serialization guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/serialization.md#headlessserializermessagepack)
