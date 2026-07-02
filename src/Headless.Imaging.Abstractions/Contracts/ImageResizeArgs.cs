// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Imaging;

/// <summary>Parameters that control an image resize operation.</summary>
public sealed class ImageResizeArgs
{
    /// <summary>Gets the target width in pixels, or <c>0</c> when only height is constrained.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value.</exception>
    public int Width
    {
        get;
        private init => field = Argument.IsPositiveOrZero(value);
    }

    /// <summary>Gets the target height in pixels, or <c>0</c> when only width is constrained.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative value.</exception>
    public int Height
    {
        get;
        private init => field = Argument.IsPositiveOrZero(value);
    }

    /// <summary>
    /// Gets the expected MIME type of the input image (for example <c>image/png</c>), or
    /// <see langword="null"/> to let the resizer auto-detect the format from the stream.
    /// When specified, contributors that do not support the given MIME type skip the image.
    /// </summary>
    public string? MimeType { get; private init; }

    /// <summary>Gets or sets the resize mode applied to this operation.</summary>
    /// <remarks>
    /// Defaults to <see cref="ImageResizeMode.Default"/>. Call
    /// <see cref="ChangeDefaultResizeMode"/> to replace the sentinel value with a real mode
    /// without overriding an explicit caller choice.
    /// </remarks>
    public ImageResizeMode Mode { get; set; } = ImageResizeMode.Default;

    /// <summary>
    /// Replaces <see cref="Mode"/> with <paramref name="defaultMode"/> only when the current
    /// value is <see cref="ImageResizeMode.Default"/>. A caller that set an explicit mode is unaffected.
    /// </summary>
    /// <param name="defaultMode">The mode to apply when no explicit mode was chosen.</param>
    public void ChangeDefaultResizeMode(ImageResizeMode defaultMode)
    {
        if (Mode is ImageResizeMode.Default)
        {
            Mode = defaultMode;
        }
    }

    /// <summary>
    /// Initializes resize arguments that only constrain the MIME type without specifying
    /// dimensions. The contributor will preserve the original size.
    /// </summary>
    /// <param name="mimeType">The expected MIME type of the input image.</param>
    /// <exception cref="ArgumentException"><paramref name="mimeType"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public ImageResizeArgs(string mimeType)
    {
        MimeType = Argument.IsNotNullOrWhiteSpace(mimeType);
    }

    /// <summary>
    /// Initializes resize arguments with a required width and an optional height.
    /// When <paramref name="height"/> is omitted, contributors that support aspect-ratio
    /// preservation will compute it automatically.
    /// </summary>
    /// <param name="mode">The resize algorithm to apply.</param>
    /// <param name="width">Target width in pixels. Must be positive.</param>
    /// <param name="height">Target height in pixels, or <see langword="null"/> to derive from width.</param>
    /// <param name="mimeType">Optional MIME type hint; <see langword="null"/> to auto-detect.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> is not positive, or <paramref name="height"/> is provided and is not positive.
    /// </exception>
    public ImageResizeArgs(ImageResizeMode mode, int width, int? height = null, string? mimeType = null)
    {
        Argument.IsPositive(height);

        Mode = Argument.IsInEnum(mode);
        Width = Argument.IsPositive(width);
        Height = height ?? 0;
        MimeType = mimeType;
    }

    /// <summary>
    /// Initializes resize arguments with a required height and an optional width.
    /// When <paramref name="width"/> is omitted, contributors that support aspect-ratio
    /// preservation will compute it automatically.
    /// </summary>
    /// <param name="mode">The resize algorithm to apply.</param>
    /// <param name="width">Target width in pixels, or <see langword="null"/> to derive from height.</param>
    /// <param name="height">Target height in pixels. Must be positive.</param>
    /// <param name="mimeType">Optional MIME type hint; <see langword="null"/> to auto-detect.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="height"/> is not positive, or <paramref name="width"/> is provided and is not positive.
    /// </exception>
    public ImageResizeArgs(ImageResizeMode mode, int? width, int height, string? mimeType = null)
    {
        Argument.IsPositive(width);

        Mode = Argument.IsInEnum(mode);
        Width = width ?? 0;
        Height = Argument.IsPositive(height);
        MimeType = mimeType;
    }

    /// <summary>
    /// Initializes resize arguments with both width and height explicitly specified.
    /// </summary>
    /// <param name="mode">The resize algorithm to apply.</param>
    /// <param name="width">Target width in pixels. Must be positive.</param>
    /// <param name="height">Target height in pixels. Must be positive.</param>
    /// <param name="mimeType">Optional MIME type hint; <see langword="null"/> to auto-detect.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is not positive.
    /// </exception>
    public ImageResizeArgs(ImageResizeMode mode, int width, int height, string? mimeType = null)
    {
        Mode = Argument.IsInEnum(mode);
        Width = Argument.IsPositive(width);
        Height = Argument.IsPositive(height);
        MimeType = mimeType;
    }
}
