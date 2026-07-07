// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Imaging;

/// <summary>Parameters that guide an image compression operation.</summary>
[PublicAPI]
public sealed class ImageCompressArgs(string? mimeType = null)
{
    /// <summary>
    /// Gets the expected MIME type of the input image (for example <c>image/jpeg</c>), or
    /// <see langword="null"/> to let the compressor auto-detect the format from the stream.
    /// When specified, contributors that do not support the given MIME type skip the image
    /// rather than attempting to decode it.
    /// </summary>
    public string? MimeType { get; private init; } = mimeType;
}
