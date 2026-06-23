---
domain: Media
packages: Media.Indexing.Abstractions, Media.Indexing
---

# Media

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Text Extraction vs. OCR](#text-extraction-vs-ocr)
    - [Provider per Format](#provider-per-format)
    - [Stream Ownership](#stream-ownership)
- [Headless.Media.Indexing.Abstractions](#headlessmediaindexingabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Media.Indexing](#headlessmediaindexing)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)

> Text extraction from documents (PDF, Word, PowerPoint) for full-text search indexing.

## Quick Orientation

Two packages:

- `Headless.Media.Indexing.Abstractions` — defines `IMediaFileTextProvider` (single-method interface)
- `Headless.Media.Indexing` — concrete extractors for PDF (PdfPig), DOCX, and PPTX (Open XML)

There is no built-in dispatcher or MIME-type routing. Register each needed extractor as `IMediaFileTextProvider` and inject `IEnumerable<IMediaFileTextProvider>` to fan out:

```csharp
builder.Services.AddSingleton<IMediaFileTextProvider, PdfMediaFileTextProvider>();
builder.Services.AddSingleton<IMediaFileTextProvider, WordDocumentMediaFileTextProvider>();
builder.Services.AddSingleton<IMediaFileTextProvider, PresentationDocumentMediaFileTextProvider>();
```

## Agent Instructions

- Install `Headless.Media.Indexing` — it transitively includes `Headless.Media.Indexing.Abstractions`.
- Three concrete providers ship: `PdfMediaFileTextProvider`, `WordDocumentMediaFileTextProvider`, `PresentationDocumentMediaFileTextProvider`. Register each one separately; there is no bulk registration helper.
- The interface (`IMediaFileTextProvider`) has a single method: `Task<string> GetTextAsync(Stream fileStream)`. There is **no** `SupportsMimeType` method on the interface. MIME-type dispatch is the caller's responsibility — maintain your own mapping (e.g., `"application/pdf"` → `PdfMediaFileTextProvider`) and select the provider before calling `GetTextAsync`.
- **PDF and non-seekable streams**: `PdfMediaFileTextProvider` requires a seekable stream. If the stream is not seekable (e.g., a blob-storage response stream), the provider automatically copies it into a `MemoryStream`. For very large PDFs arriving from non-seekable sources, this means the entire file is loaded into memory. Prefer seekable streams when possible.
- **Word/PowerPoint are synchronous**: `WordDocumentMediaFileTextProvider` and `PresentationDocumentMediaFileTextProvider` wrap synchronous Open XML operations in `Task.FromResult` — they do not perform actual async I/O. If CPU load from these providers is a concern in high-throughput scenarios, offload to a background thread.
- This package extracts **embedded text only** — it does not perform OCR. Scanned PDFs or image-only documents will return empty or incomplete text.
- For unsupported formats, implement `IMediaFileTextProvider` and register it alongside the built-in providers. No factory or registration extension exists for custom providers.
- Do not reference concrete provider types (`PdfMediaFileTextProvider` etc.) in application service constructors — inject `IEnumerable<IMediaFileTextProvider>` and dispatch by format.

## Core Concepts

### Text Extraction vs. OCR

These providers extract the **embedded character data** from documents — the text a PDF author encoded, the paragraphs in a DOCX body, the text frames on PPTX slides. They do **not** run OCR. A scanned PDF (image-only pages) will produce empty output; a DOCX with text-in-image elements will omit those elements. If OCR is required, combine this domain with a dedicated OCR library upstream.

### Provider per Format

The abstraction models each format handler as a separate service implementing `IMediaFileTextProvider`. Rather than a single dispatcher with a strategy table, each provider is a singleton registered under the same interface. This keeps each implementation independently replaceable and testable. The caller selects the right provider — typically by maintaining a `Dictionary<string, IMediaFileTextProvider>` keyed on MIME type.

### Stream Ownership

The providers **do not dispose** the stream passed to `GetTextAsync`. The caller retains ownership and is responsible for disposing it. For PDF, the provider may internally copy a non-seekable stream into a transient `MemoryStream` (which it disposes internally). For Word and PowerPoint, the stream is passed directly to Open XML's `Open` method and is expected to remain open and readable for the duration of the call.

---

## Headless.Media.Indexing.Abstractions

Defines the interface for extracting text from media files for indexing.

### Problem Solved

Provides a single, format-agnostic contract (`IMediaFileTextProvider`) for extracting textual content from document streams. Application code depends on this interface only; concrete format implementations are provided by `Headless.Media.Indexing` or custom implementations.

### Key Features

- `IMediaFileTextProvider` — single-method interface: `Task<string> GetTextAsync(Stream fileStream)`
- Stream-based API keeps format-parsing details out of application code
- No MIME-type coupling in the interface — format selection is the caller's responsibility

### Installation

```bash
dotnet add package Headless.Media.Indexing.Abstractions
```

### Quick Start

```csharp
// Application service depending only on the abstraction
public sealed class DocumentIndexer(IEnumerable<IMediaFileTextProvider> providers)
{
    private static readonly Dictionary<string, Type> _mimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = typeof(PdfMediaFileTextProvider),
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = typeof(WordDocumentMediaFileTextProvider),
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = typeof(PresentationDocumentMediaFileTextProvider),
    };

    public async Task<string?> ExtractTextAsync(Stream fileStream, string mimeType, CancellationToken ct = default)
    {
        if (!_mimeMap.TryGetValue(mimeType, out var providerType))
        {
            return null;
        }

        var provider = providers.FirstOrDefault(p => p.GetType() == providerType);
        if (provider is null) return null;

        return await provider.GetTextAsync(fileStream).ConfigureAwait(false);
    }
}
```

### Configuration

None. This is an abstractions-only package with no configuration.

### Dependencies

None.

### Side Effects

None.

---

## Headless.Media.Indexing

Concrete text extraction implementations for PDF, Word (.docx), and PowerPoint (.pptx) documents.

### Problem Solved

Provides text extraction from common document formats for full-text search indexing, using `PdfPig` for PDFs and `DocumentFormat.OpenXml` for Office formats.

### Key Features

- `PdfMediaFileTextProvider` — PDF text extraction via PdfPig; handles non-seekable streams transparently by buffering to `MemoryStream`
- `WordDocumentMediaFileTextProvider` — DOCX body text extraction via Open XML (paragraphs from `MainDocumentPart`)
- `PresentationDocumentMediaFileTextProvider` — PPTX slide text extraction via Open XML (all text frames across all slides)
- Stream-based API — providers do not dispose the caller's stream

### Design Notes

**PdfPig instead of iText**: PDF extraction uses [PdfPig](https://github.com/UglyToad/PdfPig) (`UglyToad.PdfPig`), an MIT-licensed pure .NET PDF reader. iText7 is AGPL-licensed, which imposes copyleft obligations on commercial applications; PdfPig avoids that constraint at no functional cost for text-extraction use cases.

**Non-seekable stream buffering**: PdfPig's parser requires a seekable stream. When the caller passes a non-seekable stream (common with blob-storage response streams), `PdfMediaFileTextProvider` copies the entire content into a `MemoryStream` before opening the document, then disposes it. Word and PowerPoint providers use Open XML's `Open` method, which does not require seekable streams.

**Synchronous Open XML wrappers**: `WordDocumentMediaFileTextProvider` and `PresentationDocumentMediaFileTextProvider` call synchronous Open XML APIs internally and return `Task.FromResult(...)`. The `async`-shaped signature satisfies the interface contract without adding overhead for formats that have no async parsing path.

### Installation

```bash
dotnet add package Headless.Media.Indexing
```

### Quick Start

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
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                => providers.OfType<WordDocumentMediaFileTextProvider>().FirstOrDefault(),
            "application/vnd.openxmlformats-officedocument.presentationml.presentation"
                => providers.OfType<PresentationDocumentMediaFileTextProvider>().FirstOrDefault(),
            _ => null,
        };

        if (provider is null) return string.Empty;

        return await provider.GetTextAsync(fileStream).ConfigureAwait(false);
    }
}
```

### Configuration

None. Providers have no configuration options; they are stateless singletons.

### Dependencies

- `Headless.Media.Indexing.Abstractions`
- `Headless.Hosting`
- `PdfPig` (PDF extraction)
- `DocumentFormat.OpenXml` (Word and PowerPoint extraction)

### Side Effects

None. No DI registrations are performed automatically; all registrations are explicit `AddSingleton` calls.
