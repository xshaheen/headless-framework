// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging;

/// <summary>What an image is, read from its headers.</summary>
[PublicAPI]
public sealed record ImageInfo
{
    /// <summary>Gets the MIME type detected from the bytes (for example <c>image/jpeg</c>).</summary>
    public required string MimeType { get; init; }

    /// <summary>
    /// Gets the width in pixels as the image displays: after the EXIF orientation, which browsers apply, turns a
    /// sideways-stored photo upright. For an animation, the width of one frame.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the height in pixels as the image displays, after the EXIF orientation. For an animation, the height of one
    /// frame.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>Gets the number of frames: <c>1</c> for a still image, more for an animated GIF or WebP.</summary>
    public required int FrameCount { get; init; }

    /// <summary>Gets a value indicating whether the image has an alpha channel, so it may contain transparency.</summary>
    public required bool HasAlpha { get; init; }

    /// <summary>Gets a value indicating whether the image has more than one frame.</summary>
    public bool IsAnimated => FrameCount > 1;
}
