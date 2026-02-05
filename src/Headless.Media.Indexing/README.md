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
