// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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
    public static ImageStreamResizeResult CannotRead() => NotSupported(CannotReadError);

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

/// <summary>Carries the output of a resize operation together with its metadata.</summary>
/// <typeparam name="TContent">The type that holds the image bytes, typically <see cref="Stream"/>.</typeparam>
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
