# Headless.Media.Indexing.Abstractions

Defines the interface for extracting text from media files for indexing.

## Problem Solved

Provides a format-agnostic contract (`IMediaFileTextProvider`) for extracting textual content from document streams, plus a resolver (`IMediaFileTextProviderResolver`) that selects the right provider by format. Application code depends on these interfaces only; concrete format implementations are provided by `Headless.Media.Indexing` or custom implementations.

## Key Features

- `IMediaFileTextProvider` — single-method interface: `Task<string> GetTextAsync(Stream fileStream, CancellationToken cancellationToken = default)`
- `IMediaFileTextProviderResolver` — `GetProvider(string fileExtensionOrMimeType)` returns the provider for a format, or `null` when unsupported
- Stream-based API keeps format-parsing details out of application code

## Installation

```bash
dotnet add package Headless.Media.Indexing.Abstractions
```

## Quick Start

```csharp
// Application service depending only on the abstraction
public sealed class DocumentIndexer(IMediaFileTextProviderResolver resolver)
{
    public async Task<string?> ExtractTextAsync(
        Stream fileStream,
        string mimeTypeOrExtension,
        CancellationToken ct = default
    )
    {
        var provider = resolver.GetProvider(mimeTypeOrExtension);

        if (provider is null)
        {
            return null;
        }

        return await provider.GetTextAsync(fileStream, ct).ConfigureAwait(false);
    }
}
```

## Configuration

None. This is an abstractions-only package with no configuration.

## Dependencies

None.

## Side Effects

None.
