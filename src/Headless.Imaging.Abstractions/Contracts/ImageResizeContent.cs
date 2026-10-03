// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Imaging;

/// <summary>Carries the output of a resize operation together with its metadata.</summary>
/// <typeparam name="TContent">The type that holds the image bytes, typically <see cref="Stream"/>.</typeparam>
[PublicAPI]
public sealed class ImageResizeContent<TContent>
{
    /// <summary>Gets the resized image content. For stream-based results this is a readable, seekable stream.</summary>
    public required TContent Content { get; init; }

    /// <summary>Gets the MIME type of the output image (for example <c>image/jpeg</c>).</summary>
    public required string MimeType { get; init; }

    /// <summary>Gets the actual width of the output image in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Gets the actual height of the output image in pixels.</summary>
    public required int Height { get; init; }
}
