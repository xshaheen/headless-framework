# Headless.Messaging.SourceGenerator

Roslyn incremental source generator that registers Messaging consumers at compile time.

## Why use this package

Without the source generator, every message consumer would have to be registered with the Messaging runtime by hand at startup. The source generator finds `[BusConsumer]` and `[QueueConsumer]` classes at compile time and emits one `MessagingModule` per assembly, with a typed dispatcher for each consumer; the module registers it with `AddModule<MessagingModule>()`, with no runtime assembly scanning.

## Install

```bash
dotnet add package Headless.Messaging.SourceGenerator
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Messaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#headlessmessagingsourcegenerator)
