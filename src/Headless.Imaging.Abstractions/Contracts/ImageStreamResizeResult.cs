// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Imaging;

/// <summary>The result of a stream-based image resize operation.</summary>
/// <remarks>
/// Check <see cref="ImageProcessResult{T}.IsDone"/> before accessing
/// <see cref="ImageProcessResult{T}.Result"/>. When <c>IsDone</c> is <see langword="true"/>,
/// <see cref="ImageProcessResult{T}.Result"/> is an <see cref="ImageResizeContent{TContent}"/>
/// whose <c>Content</c> is a readable, seekable <see cref="Stream"/> containing the resized bytes.
/// Callers are responsible for disposing that stream.
/// </remarks>
[PublicAPI]
public sealed class ImageStreamResizeResult : ImageProcessResult<ImageResizeContent<Stream>>
{
    private ImageStreamResizeResult() { }

    /// <summary>Creates an <see cref="ImageProcessState.Unsupported"/> result indicating the stream could not be read.</summary>
    /// <returns>A result with <see cref="ImageProcessState.Unsupported"/> state.</returns>
    public static ImageStreamResizeResult CannotRead()
    {
        return NotSupported(CannotReadError);
    }

    /// <summary>Creates an <see cref="ImageProcessState.Unsupported"/> result for an unsupported MIME type.</summary>
    /// <param name="mimType">The MIME type that is not supported.</param>
    /// <returns>A result with <see cref="ImageProcessState.Unsupported"/> state.</returns>
    public static ImageStreamResizeResult NotSupportedMimeType(string mimType)
    {
        return NotSupported($"The given MIME type {mimType} is not supported.");
    }

    /// <summary>Creates an <see cref="ImageProcessState.Unsupported"/> result with a custom error message.</summary>
    /// <param name="error">A description of why the format is not supported.</param>
    /// <returns>A result with <see cref="ImageProcessState.Unsupported"/> state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static ImageStreamResizeResult NotSupported(string error = UnsupportedError)
    {
        return new() { State = ImageProcessState.Unsupported, Error = Argument.IsNotNull(error) };
    }

    /// <summary>Creates an <see cref="ImageProcessState.Failed"/> result with a custom error message.</summary>
    /// <param name="error">A description of why resizing failed.</param>
    /// <returns>A result with <see cref="ImageProcessState.Failed"/> state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static ImageStreamResizeResult Failed(string error = FailedError)
    {
        return new() { State = ImageProcessState.Failed, Error = Argument.IsNotNull(error) };
    }

    /// <summary>Creates a successful <see cref="ImageProcessState.Done"/> result.</summary>
    /// <param name="content">A readable stream containing the resized image bytes.</param>
    /// <param name="mimeType">The MIME type of the output image.</param>
    /// <param name="width">The actual width of the output image in pixels.</param>
    /// <param name="height">The actual height of the output image in pixels.</param>
    /// <returns>A result with <see cref="ImageProcessState.Done"/> state.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/> or <paramref name="mimeType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is not positive.
    /// </exception>
    public static ImageStreamResizeResult Done(Stream content, string mimeType, int width, int height)
    {
        return new()
        {
            State = ImageProcessState.Done,
            Result = new()
            {
                Content = Argument.IsNotNull(content),
                MimeType = Argument.IsNotNull(mimeType),
                Width = Argument.IsPositive(width),
                Height = Argument.IsPositive(height),
            },
        };
    }
}
