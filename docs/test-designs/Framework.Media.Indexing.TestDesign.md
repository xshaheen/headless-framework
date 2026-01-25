# Test Case Design: Framework.Media.Indexing + Abstractions

**Packages:**
- `src/Framework.Media.Indexing.Abstractions`
- `src/Framework.Media.Indexing`

**Test Project:** `tests/Framework.Media.Indexing.Tests.Unit`
**Generated:** 2026-01-25

## Package Analysis

### Framework.Media.Indexing.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `IMediaFileTextProvider.cs` | Interface for extracting text from media files | Low (interface) |

### Framework.Media.Indexing

| File | Purpose | Testable |
|------|---------|----------|
| `PdfMediaFileTextProvider.cs` | Extracts text from PDF using PdfPig; handles non-seekable streams | High |
| `WordDocumentMediaFileTextProvider.cs` | Extracts text from .docx using OpenXml | High |
| `PresentationDocumentMediaFileTextProvider.cs` | Extracts text from .pptx using OpenXml | High |

---

## Current Test Coverage

### PdfMediaFileTextProviderTests (1 test)

| Test Case | Status |
|-----------|--------|
| `get_text_async_should_extract_text_from_real_pdf_file` | Exists |

**Missing:**
- Non-seekable stream handling (MemoryStream copy path)
- Empty PDF handling
- Multi-page PDF

### WordDocumentMediaFileTextProviderTests (4 tests)

| Test Case | Status |
|-----------|--------|
| `get_text_async_should_extract_text_from_word_file` | Exists |
| `get_text_async_should_return_empty_string_when_document_has_no_paragraphs` | Exists |
| `get_text_async_should_return_paragraph_text_when_document_has_single_paragraph` | Exists |
| `get_text_async_should_return_all_paragraphs_when_document_has_multiple_paragraphs` | Exists |

**Coverage:** Good - covers empty, single, and multiple paragraphs.

### PresentationDocumentMediaFileTextProviderTests (4 tests)

| Test Case | Status |
|-----------|--------|
| `get_text_async_should_extract_text_from_power_point_file` | Exists |
| `get_text_async_should_return_empty_string_when_presentation_has_no_slides` | Exists |
| `get_text_async_should_return_text_from_single_slide` | Exists |
| `get_text_async_should_return_text_from_multiple_slides` | Exists |

**Coverage:** Good - covers empty, single, and multiple slides.

---

## Missing: PdfMediaFileTextProvider Tests

**File:** `tests/Framework.Media.Indexing.Tests.Unit/PdfMediaFileTextProviderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_extract_text_from_non_seekable_stream` | Verifies MemoryStream copy logic when `CanSeek=false` |
| `should_return_empty_string_for_empty_pdf` | PDF with no pages/text |
| `should_extract_text_from_multi_page_pdf` | Verify all pages concatenated |
| `should_dispose_memory_stream_after_processing_non_seekable_stream` | Resource cleanup |

---

## Missing: Edge Case Tests (All Providers)

| Provider | Test Case | Description |
|----------|-----------|-------------|
| Word | `should_handle_document_without_main_document_part` | `MainDocumentPart` is null |
| Word | `should_handle_document_with_null_body` | Document body is null |
| Presentation | `should_handle_slide_without_relationship_id` | `RelationshipId` is null |
| Presentation | `should_handle_slide_part_with_no_text` | Slide has shapes but no text |
| Presentation | `should_handle_multiple_paragraphs_in_slide` | Multiple text runs |

---

## Test Summary

| Component | Existing | New Unit | Total |
|-----------|----------|----------|-------|
| IMediaFileTextProvider | 0 | 0 | 0 |
| PdfMediaFileTextProvider | 1 | 4 | 5 |
| WordDocumentMediaFileTextProvider | 4 | 2 | 6 |
| PresentationDocumentMediaFileTextProvider | 4 | 3 | 7 |
| **Total** | **9** | **9** | **18** |

---

## Priority Order

1. **PdfMediaFileTextProvider non-seekable stream** - Critical untested code path (Azure Blob Storage scenario)
2. **PdfMediaFileTextProvider multi-page** - Common use case
3. **Null/empty edge cases** - Defensive programming validation

---

## Notes

1. **Non-seekable stream handling** - `PdfMediaFileTextProvider` has special logic for non-seekable streams (copies to MemoryStream). This is the primary untested code path.

2. **Test file dependencies** - Tests use real files in `tests/Framework.Media.Indexing.Tests.Unit/Files/`:
   - `TestPdf.pdf`
   - `TestWORD.docx`
   - `TestPPTX.pptx`

3. **OpenXml document creation** - Word and Presentation tests create in-memory documents using OpenXml SDK, avoiding file dependencies for edge cases.

4. **Snapshot testing** - PDF test uses Verify for snapshot assertion.

5. **Interface-only package** - `Framework.Media.Indexing.Abstractions` contains only `IMediaFileTextProvider` interface with no testable logic.

---

## Non-Seekable Stream Test Example

```csharp
[Fact]
public async Task should_extract_text_from_non_seekable_stream()
{
    // given
    var pdfFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "Files", "TestPdf.pdf"
    );
    var bytes = await File.ReadAllBytesAsync(pdfFilePath);
    await using var nonSeekableStream = new NonSeekableMemoryStream(bytes);

    // when
    var result = await _sut.GetTextAsync(nonSeekableStream);

    // then
    result.Should().NotBeEmpty();
}

private sealed class NonSeekableMemoryStream(byte[] buffer) : MemoryStream(buffer)
{
    public override bool CanSeek => false;
}
```

---

## Recommendation

**Medium Priority** - Current coverage is good for happy paths. Focus on:
1. Non-seekable stream path in PDF provider (untested critical path)
2. Null-safety edge cases for defensive programming validation
