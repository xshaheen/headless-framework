# Headless.Media.Indexing

Concrete text extraction implementations for PDF, Word (.docx), and PowerPoint (.pptx) documents.

## Problem Solved

Provides text extraction from common document formats for full-text search indexing, using `PdfPig` for PDFs and `DocumentFormat.OpenXml` for Office formats.

## Key Features

- `PdfMediaFileTextProvider` — PDF text extraction via PdfPig; handles non-seekable streams transparently by buffering to `MemoryStream`
- `WordDocumentMediaFileTextProvider` — DOCX body text extraction via Open XML (paragraphs from `MainDocumentPart`)
- `PresentationDocumentMediaFileTextProvider` — PPTX slide text extraction via Open XML (all text frames across all slides)
- `MediaFileTextProviderResolver` — `IMediaFileTextProviderResolver` that dispatches to the three providers by extension or MIME type
- `SetupMediaIndexing.AddMediaIndexing()` — registers the three providers plus the resolver in one call
- Stream-based API — providers do not dispose the caller's stream

## Design Notes

**PdfPig instead of iText**: PDF extraction uses [PdfPig](https://github.com/UglyToad/PdfPig) (`UglyToad.PdfPig`), an MIT-licensed pure .NET PDF reader. iText7 is AGPL-licensed, which imposes copyleft obligations on commercial applications; PdfPig avoids that constraint at no functional cost for text-extraction use cases.

**Non-seekable stream buffering**: PdfPig's parser requires a seekable stream. When the caller passes a non-seekable stream (common with blob-storage response streams), `PdfMediaFileTextProvider` copies the entire content into a `MemoryStream` before opening the document, then disposes it. Word and PowerPoint providers use Open XML's `Open` method, which does not require seekable streams.

**Synchronous Open XML wrappers**: `WordDocumentMediaFileTextProvider` and `PresentationDocumentMediaFileTextProvider` call synchronous Open XML APIs internally and return `Task.FromResult(...)`. The `async`-shaped signature satisfies the interface contract without adding overhead for formats that have no async parsing path.

## Installation

```bash
dotnet add package Headless.Media.Indexing
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Registers the three providers plus IMediaFileTextProviderResolver.
builder.Services.AddMediaIndexing();
```

Dispatch by format with the resolver:

```csharp
public sealed class SearchIndexer(IMediaFileTextProviderResolver resolver)
{
    public async Task<string> IndexDocumentAsync(
        Stream fileStream,
        string mimeTypeOrExtension,
        CancellationToken cancellationToken
    )
    {
        // Accepts "application/pdf", "pdf", or ".pdf" — returns null for unsupported formats.
        var provider = resolver.GetProvider(mimeTypeOrExtension);

        if (provider is null)
        {
            return string.Empty;
        }

        return await provider.GetTextAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }
}
```

## Configuration

None. Providers have no configuration options; they are stateless singletons.

## Dependencies

- `Headless.Media.Indexing.Abstractions`
- `Headless.Hosting`
- `PdfPig` (PDF extraction)
- `DocumentFormat.OpenXml` (Word and PowerPoint extraction)

## Side Effects

`AddMediaIndexing()` registers `PdfMediaFileTextProvider`, `WordDocumentMediaFileTextProvider`, and `PresentationDocumentMediaFileTextProvider` as singletons (each also as an `IMediaFileTextProvider` enumerable entry) plus `IMediaFileTextProviderResolver`, all via `TryAdd` / `TryAddEnumerable`.
