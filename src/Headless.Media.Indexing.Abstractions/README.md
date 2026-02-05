# Headless.Media.Indexing.Abstractions

Defines the interface for extracting text from media files for indexing.

## Problem Solved

Provides a unified API for extracting textual content from various document formats (PDF, Word, PowerPoint), enabling full-text search indexing of uploaded files.

## Key Features

- `IMediaFileTextProvider` - Interface for text extraction
- Stream-based API for memory efficiency
- Async support for large file processing

## Installation

```bash
dotnet add package Headless.Media.Indexing.Abstractions
```

## Usage

```csharp
public sealed class DocumentIndexer(IEnumerable<IMediaFileTextProvider> providers)
{
    public async Task<string> ExtractTextAsync(Stream fileStream, string mimeType)
    {
        var provider = providers.FirstOrDefault(p => p.SupportsMimeType(mimeType));
        if (provider is null) return string.Empty;

        return await provider.GetTextAsync(fileStream).ConfigureAwait(false);
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
