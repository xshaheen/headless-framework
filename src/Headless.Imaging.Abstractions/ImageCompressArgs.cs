// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    /// <summary>
    /// Gets the MIME type to encode the output in (for example <c>image/webp</c>), or <see langword="null"/> to keep
    /// the input format. A contributor that cannot write the requested type returns
    /// <see cref="ImageProcessState.Unsupported"/>.
    /// </summary>
    /// <remarks>
    /// Compression still succeeds only when the output is smaller than the input, so converting to a more efficient
    /// format is the usual reason to set this.
    /// </remarks>
    public string? OutputMimeType { get; init; }
}
