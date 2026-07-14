// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Constants;

namespace Headless.Media.Indexing;

/// <summary>
/// Default <see cref="IMediaFileTextProviderResolver"/> that dispatches to the three built-in providers
/// (PDF, Word, PowerPoint) by file extension or MIME type.
/// </summary>
/// <param name="pdfProvider">The PDF text provider.</param>
/// <param name="wordProvider">The Word (.docx) text provider.</param>
/// <param name="presentationProvider">The PowerPoint (.pptx) text provider.</param>
internal sealed class MediaFileTextProviderResolver(
    PdfMediaFileTextProvider pdfProvider,
    WordDocumentMediaFileTextProvider wordProvider,
    PresentationDocumentMediaFileTextProvider presentationProvider
) : IMediaFileTextProviderResolver
{
    /// <inheritdoc />
    public IMediaFileTextProvider? GetProvider(string fileExtensionOrMimeType)
    {
        Argument.IsNotNullOrWhiteSpace(fileExtensionOrMimeType);

        var key = fileExtensionOrMimeType.Trim().TrimStart('.').ToLowerInvariant();

        return key switch
        {
            "pdf" or ContentTypes.Applications.Pdf => pdfProvider,
            "docx" or ContentTypes.Applications.Docx => wordProvider,
            "pptx" or ContentTypes.Applications.PPtx => presentationProvider,
            _ => null,
        };
    }
}
