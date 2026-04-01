---
domain: Media
packages: Media.Indexing.Abstractions, Media.Indexing
---

# Media

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.Media.Indexing.Abstractions](#headlessmediaindexingabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Usage](#usage)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Media.Indexing](#headlessmediaindexing)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start)
    - [Usage](#usage-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)

> Text extraction from documents (PDF, Word, PowerPoint) for full-text search indexing.

## Quick Orientation

Two packages:

- `Headless.Media.Indexing.Abstractions` — `IMediaFileTextProvider` interface
- `Headless.Media.Indexing` — implementations for PDF (iText), DOCX, PPTX (Open XML)

Typical registration:

```csharp
builder.Services.AddSingleton<IMediaFileTextProvider, PdfMediaFileTextProvider>();
builder.Services.AddSingleton<IMediaFileTextProvider, WordDocumentMediaFileTextProvider>();
builder.Services.AddSingleton<IMediaFileTextProvider, PresentationDocumentMediaFileTextProvider>();
```

Inject `IEnumerable<IMediaFileTextProvider>` and select the right provider by MIME type using `SupportsMimeType()`.

## Agent Instructions

- Install `Headless.Media.Indexing` for the implementations — it transitively includes Abstractions.
- Three providers available: `PdfMediaFileTextProvider`, `WordDocumentMediaFileTextProvider`, `PresentationDocumentMediaFileTextProvider`.
- Register each needed provider as `IMediaFileTextProvider` singleton. There is no bulk registration method.
- Use `provider.SupportsMimeType(mimeType)` to select the correct provider for a given file.
- Call `provider.GetTextAsync(stream)` for extraction. The API is stream-based — pass the file stream directly, do not load entire files into memory.
- PDF extraction uses iText; Word/PowerPoint extraction uses DocumentFormat.OpenXml. These are transitive dependencies.
- For unsupported formats, implement `IMediaFileTextProvider` and register it alongside the built-in providers.

---

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

## None.

# Headless.Media.Indexing

Text extraction implementations for common document formats.

## Problem Solved

Provides text extraction from PDF, Word (.docx), and PowerPoint (.pptx) documents for full-text search indexing, using industry-standard libraries.

## Key Features

- `PdfMediaFileTextProvider` - PDF text extraction via iText
- `WordDocumentMediaFileTextProvider` - Word document text extraction via Open XML
- `PresentationDocumentMediaFileTextProvider` - PowerPoint text extraction via Open XML
- Stream-based processing for memory efficiency

## Installation

```bash
dotnet add package Headless.Media.Indexing
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register text providers
builder.Services.AddSingleton<IMediaFileTextProvider, PdfMediaFileTextProvider>();
builder.Services.AddSingleton<IMediaFileTextProvider, WordDocumentMediaFileTextProvider>();
builder.Services.AddSingleton<IMediaFileTextProvider, PresentationDocumentMediaFileTextProvider>();
```

## Usage

```csharp
public class SearchIndexer(PdfMediaFileTextProvider pdfProvider)
{
    public async Task IndexPdfAsync(Stream pdfStream)
    {
        var text = await pdfProvider.GetTextAsync(pdfStream).ConfigureAwait(false);
        // Index extracted text...
    }
}
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Media.Indexing.Abstractions`
- `itext7` (PDF)
- `DocumentFormat.OpenXml` (Word, PowerPoint)

## Side Effects

None.
