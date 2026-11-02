// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DocumentFormat.OpenXml.Packaging;

namespace Headless.Media.Indexing;

/// <summary>
/// Size limits that keep a small, hostile upload from exhausting memory during text extraction.
/// </summary>
/// <remarks>
/// A .docx or .pptx is a zip, so a few kilobytes can inflate to gigabytes of XML, and a non-seekable input is buffered
/// whole before parsing. Each limit sits far above the size of a real document's text.
/// </remarks>
internal static class MediaTextLimits
{
    /// <summary>The most characters the Open XML SDK reads from one decompressed part (about 32 MB of text).</summary>
    public const long MaxCharactersInPart = 16L * 1024 * 1024;

    /// <summary>The most characters one extraction returns.</summary>
    public const int MaxExtractedCharacters = 16 * 1024 * 1024;

    /// <summary>The most bytes buffered from a non-seekable input stream.</summary>
    public const long MaxBufferedBytes = 100L * 1024 * 1024;

    /// <summary>Open settings that bound every part the Open XML SDK loads.</summary>
    public static OpenSettings OpenXmlSettings => new() { MaxCharactersInPart = MaxCharactersInPart };

    /// <summary>
    /// Copies a non-seekable <paramref name="source"/> into memory, asynchronously and capped at
    /// <see cref="MaxBufferedBytes"/>, because every parser here needs a seekable stream; returns
    /// <see langword="null"/> when <paramref name="source"/> is already seekable.
    /// </summary>
    /// <remarks>
    /// Handing a non-seekable stream to the Open XML SDK or PdfPig makes them copy it synchronously and without a
    /// limit, blocking a thread-pool thread for the whole download. A known <paramref name="contentLength"/> sizes the
    /// buffer once and refuses an oversize input before any byte is read; the copy still enforces the cap, since the
    /// reported length can be wrong.
    /// </remarks>
    /// <exception cref="InvalidDataException">The input exceeds <see cref="MaxBufferedBytes"/>.</exception>
    public static async Task<MemoryStream?> BufferIfNotSeekableAsync(
        Stream source,
        long? contentLength,
        CancellationToken cancellationToken
    )
    {
        if (source.CanSeek)
        {
            return null;
        }

        if (contentLength > MaxBufferedBytes)
        {
            throw _TooLarge();
        }

        var destination = contentLength is { } length and > 0 ? new MemoryStream((int)length) : new MemoryStream();

        try
        {
            var buffer = new byte[81920];
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (destination.Length + read > MaxBufferedBytes)
                {
                    throw _TooLarge();
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            destination.Position = 0;

            return destination;
        }
        catch
        {
            await destination.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        static InvalidDataException _TooLarge()
        {
            return new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The non-seekable input stream exceeds {MaxBufferedBytes} bytes, the most a media text provider buffers. Pass a seekable stream instead."
                )
            );
        }
    }

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
