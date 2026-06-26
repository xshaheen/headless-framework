# Headless.Media.Indexing.Abstractions

Defines the interface for extracting text from media files for indexing.

## Problem Solved

Provides a single, format-agnostic contract (`IMediaFileTextProvider`) for extracting textual content from document streams. Application code depends on this interface only; concrete format implementations are provided by `Headless.Media.Indexing` or custom implementations.

## Key Features

- `IMediaFileTextProvider` — single-method interface: `Task<string> GetTextAsync(Stream fileStream)`
- Stream-based API keeps format-parsing details out of application code
- No MIME-type coupling in the interface — format selection is the caller's responsibility

## Installation

```bash
dotnet add package Headless.Media.Indexing.Abstractions
```

## Quick Start

```csharp
// Application service depending only on the abstraction
public sealed class DocumentIndexer(IEnumerable<IMediaFileTextProvider> providers)
{
    private static readonly Dictionary<string, Type> _mimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = typeof(PdfMediaFileTextProvider),
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] =
            typeof(WordDocumentMediaFileTextProvider),
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] =
            typeof(PresentationDocumentMediaFileTextProvider),
    };

    public async Task<string?> ExtractTextAsync(Stream fileStream, string mimeType, CancellationToken ct = default)
    {
        if (!_mimeMap.TryGetValue(mimeType, out var providerType))
        {
            return null;
        }

        var provider = providers.FirstOrDefault(p => p.GetType() == providerType);
        if (provider is null)
            return null;

        return await provider.GetTextAsync(fileStream).ConfigureAwait(false);
    }
}
```

## Configuration

None. This is an abstractions-only package with no configuration.

## Dependencies

None.

## Side Effects

None.
