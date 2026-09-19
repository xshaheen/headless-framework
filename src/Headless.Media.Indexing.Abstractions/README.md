# Headless.Media.Indexing.Abstractions

Defines the interface for extracting text from media files for indexing.

## Why use this package

Provides a format-agnostic contract (`IMediaFileTextProvider`) for extracting textual content from document streams, plus a resolver (`IMediaFileTextProviderResolver`) that selects the right provider by format. Application code depends on these interfaces only; concrete format implementations are provided by `Headless.Media.Indexing` or custom implementations.

## Install

```bash
dotnet add package Headless.Media.Indexing.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Media guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/media.md#headlessmediaindexingabstractions)
