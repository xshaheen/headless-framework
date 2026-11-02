// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DocumentFormat.OpenXml.Packaging;

namespace Headless.Media.Indexing;

/// <summary>
/// Size limits that keep a small, hostile upload from exhausting memory during text extraction.
/// </summary>
/// <remarks>
/// A .docx or .pptx is a zip, so a few kilobytes can inflate to gigabytes of XML, and a non-seekable PDF is buffered
/// whole before parsing. Each limit sits far above the size of a real document's text.
/// </remarks>
internal static class MediaTextLimits
{
    /// <summary>The most characters the Open XML SDK reads from one decompressed part (about 32 MB of text).</summary>
    public const long MaxCharactersInPart = 16L * 1024 * 1024;

    /// <summary>The most characters one extraction returns.</summary>
    public const int MaxExtractedCharacters = 16 * 1024 * 1024;

    /// <summary>The most bytes buffered from a non-seekable PDF stream.</summary>
    public const long MaxBufferedPdfBytes = 100L * 1024 * 1024;

    /// <summary>Open settings that bound every part the Open XML SDK loads.</summary>
    public static OpenSettings OpenXmlSettings => new() { MaxCharactersInPart = MaxCharactersInPart };

    /// <summary>Appends <paramref name="text"/>, refusing once the extraction passes <see cref="MaxExtractedCharacters"/>.</summary>
    /// <exception cref="InvalidDataException">The extracted text exceeds <see cref="MaxExtractedCharacters"/>.</exception>
    public static void AppendBounded(StringBuilder builder, string? text)
    {
        if (builder.Length + (text?.Length ?? 0) > MaxExtractedCharacters)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The document's text exceeds {MaxExtractedCharacters} characters, the most a media text provider extracts."
                )
            );
        }

        builder.Append(text);
    }
}
