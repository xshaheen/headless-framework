---
domain: Media
packages: Media.Indexing.Abstractions, Media.Indexing
---

# Media

> Extracts embedded text from PDF, Word (.docx), and PowerPoint (.pptx) files for full-text search indexing.

## Orientation

Install `Headless.Media.Indexing`. It brings `Headless.Media.Indexing.Abstractions` with it. Both packages use the `Headless.Media.Indexing` namespace.

- `IMediaFileTextProvider` extracts text from one format: `Task<string> GetTextAsync(Stream fileStream, long? contentLength = null, CancellationToken cancellationToken = default)`.
- `IMediaFileTextProviderResolver.GetProvider(string fileExtensionOrMimeType)` returns the provider for a format, or `null` when no provider handles it.
- `AddMediaIndexing()` registers the three providers (`PdfMediaFileTextProvider`, `WordDocumentMediaFileTextProvider`, `PresentationDocumentMediaFileTextProvider`) and the resolver.

## Agent Rules

- Inject `IMediaFileTextProviderResolver` and call `GetProvider(...)` to pick a provider by format. Do not inject a concrete provider type, and do not write your own MIME-to-provider map. `IMediaFileTextProvider` has no `SupportsMimeType` method.
- Pass `contentLength` when you know it, such as `BlobDownloadResult.ContentLength` from a blob download. A non-seekable stream is then buffered at its final size, and a stated length over 100 MiB fails before any byte is read.
- Catch `InvalidDataException` and `System.Xml.XmlException` to treat an oversized or malformed upload as unindexable. The size limits are fixed and have no option. See [Limits for untrusted uploads](#limits-for-untrusted-uploads).
- Expect empty text from a scanned PDF or an image-only document. The providers read embedded text and do not run OCR. Run an OCR library upstream when you need text from images.
- Dispose the stream yourself. The providers never dispose the stream you pass. Keep a seekable stream open and readable until `GetTextAsync` completes.
- Offload extraction from a request thread when throughput matters. Parsing is synchronous and CPU-bound, and `cancellationToken` cannot interrupt the parse of one page or the initial document open.

---

## Headless.Media.Indexing.Abstractions

The `IMediaFileTextProvider` and `IMediaFileTextProviderResolver` contracts. Reference it alone from a library that consumes extracted text and leaves registration to the host. It has no configuration.

```csharp
public sealed class DocumentIndexer(IMediaFileTextProviderResolver resolver)
{
    public async Task<string?> ExtractTextAsync(
        Stream fileStream,
        string mimeTypeOrExtension,
        long? contentLength,
        CancellationToken ct = default
    )
    {
        var provider = resolver.GetProvider(mimeTypeOrExtension);

        if (provider is null)
        {
            return null;
        }

        return await provider.GetTextAsync(fileStream, contentLength, ct).ConfigureAwait(false);
    }
}
```

---

## Headless.Media.Indexing

The PDF, Word, and PowerPoint text providers and their registration.

### Setup

```bash
dotnet add package Headless.Media.Indexing
```

```csharp
builder.Services.AddMediaIndexing();
```

**Verify:** no startup signal. In a test, build the service provider and assert that `GetRequiredService<IMediaFileTextProviderResolver>().GetProvider("pdf")` is not `null`.

`AddMediaIndexing()` registers each provider as a singleton, both as its concrete type and as an `IMediaFileTextProvider` enumerable entry, and registers the resolver as a singleton. Every registration uses `TryAdd` or `TryAddEnumerable`, so a second call does nothing. To change format dispatch, register your own `IMediaFileTextProviderResolver` before you call `AddMediaIndexing()`.

### Format resolution

`GetProvider` trims the input, drops a leading `.`, and compares without case. It throws `ArgumentException` for a null, empty, or whitespace argument.

| Format | Accepted keys | Provider | Text it reads |
| --- | --- | --- | --- |
| PDF | `pdf`, `application/pdf` | `PdfMediaFileTextProvider` (PdfPig) | Text on every page |
| Word | `docx`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | `WordDocumentMediaFileTextProvider` (Open XML) | Paragraphs of the main document body |
| PowerPoint | `pptx`, `application/vnd.openxmlformats-officedocument.presentationml.presentation` | `PresentationDocumentMediaFileTextProvider` (Open XML) | Text frames on every slide |

The resolver maps only these three formats. To support another format, implement `IMediaFileTextProvider`, register it, and dispatch to it in your own code. The built-in resolver never returns it, but `IEnumerable<IMediaFileTextProvider>` includes it.

The PDF provider uses PdfPig, which is MIT-licensed, so it adds no AGPL obligation the way iText would.

### Streams and cancellation

PdfPig and Open XML need a seekable stream. Given a non-seekable stream, such as a blob response stream or a request body, a provider first copies it asynchronously into a `MemoryStream`, then disposes that copy when it finishes. A seekable stream goes to the parser directly.

`cancellationToken` is checked while the provider buffers a non-seekable stream and between pages, paragraphs, or slides. A cancelled call throws `OperationCanceledException`.

### Limits for untrusted uploads

A .docx or .pptx file is a zip, and a PDF's Flate and LZW streams are compressed, so a small upload can inflate to gigabytes. Every limit below is fixed.

| Limit | Applies to | Exception |
| --- | --- | --- |
| 100 MiB buffered from a non-seekable stream, or a stated `contentLength` over 100 MiB | All providers | `InvalidDataException` |
| 100 MiB decoded from any one Flate or LZW stream, checked before PdfPig decodes it | PDF | `InvalidDataException` |
| 16 Mi characters in any one decompressed part | Word, PowerPoint | `System.Xml.XmlException` |
| 16 Mi characters of extracted text in total | All providers | `InvalidDataException` |
