# Headless.Media.Indexing

Concrete text extraction implementations for PDF, Word (.docx), and PowerPoint (.pptx) documents.

## Problem Solved

Provides text extraction from common document formats for full-text search indexing, using `PdfPig` for PDFs and `DocumentFormat.OpenXml` for Office formats.

## Key Features

- `PdfMediaFileTextProvider` — PDF text extraction via PdfPig; handles non-seekable streams transparently by buffering to `MemoryStream`
- `WordDocumentMediaFileTextProvider` — DOCX body text extraction via Open XML (paragraphs from `MainDocumentPart`)
- `PresentationDocumentMediaFileTextProvider` — PPTX slide text extraction via Open XML (all text frames across all slides)
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

// Register each provider you need. No bulk helper exists.
builder.Services.AddSingleton<IMediaFileTextProvider, PdfMediaFileTextProvider>();
builder.Services.AddSingleton<IMediaFileTextProvider, WordDocumentMediaFileTextProvider>();
builder.Services.AddSingleton<IMediaFileTextProvider, PresentationDocumentMediaFileTextProvider>();
```

Dispatch by format in a service that injects `IEnumerable<IMediaFileTextProvider>`:

```csharp
public sealed class SearchIndexer(IEnumerable<IMediaFileTextProvider> providers)
{
    public async Task<string> IndexDocumentAsync(Stream fileStream, string mimeType)
    {
        // Caller owns format → provider mapping; no SupportsMimeType() method exists.
        var provider = mimeType switch
        {
            "application/pdf" => providers.OfType<PdfMediaFileTextProvider>().FirstOrDefault(),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => providers
                .OfType<WordDocumentMediaFileTextProvider>()
                .FirstOrDefault(),
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => providers
                .OfType<PresentationDocumentMediaFileTextProvider>()
                .FirstOrDefault(),
            _ => null,
        };

        if (provider is null)
            return string.Empty;

        return await provider.GetTextAsync(fileStream).ConfigureAwait(false);
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

None. No DI registrations are performed automatically; all registrations are explicit `AddSingleton` calls.
