// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Imaging;

/// <summary>The result of a stream-based image compression operation.</summary>
/// <remarks>
/// Check <see cref="ImageProcessResult{T}.IsDone"/> before accessing
/// <see cref="ImageProcessResult{T}.Result"/>. When <c>IsDone</c> is <see langword="true"/>,
/// <c>Result</c> is a readable, seekable <see cref="Stream"/> containing the compressed bytes.
/// Callers are responsible for disposing that stream.
/// </remarks>
public sealed class ImageStreamCompressResult : ImageProcessResult<Stream>
{
    private ImageStreamCompressResult() { }

    /// <summary>Creates an <see cref="ImageProcessState.Unsupported"/> result indicating the stream could not be read.</summary>
    /// <returns>A result with <see cref="ImageProcessState.Unsupported"/> state.</returns>
    public static ImageStreamCompressResult CannotRead() => NotSupported(CannotReadError);

    /// <summary>Creates an <see cref="ImageProcessState.Unsupported"/> result with a custom error message.</summary>
    /// <param name="error">A description of why the format is not supported.</param>
    /// <returns>A result with <see cref="ImageProcessState.Unsupported"/> state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static ImageStreamCompressResult NotSupported(string error = UnsupportedError)
    {
        return new() { State = ImageProcessState.Unsupported, Error = Argument.IsNotNull(error) };
    }

    /// <summary>Creates an <see cref="ImageProcessState.Unsupported"/> result for an unsupported MIME type.</summary>
    /// <param name="mimType">The MIME type that is not supported.</param>
    /// <returns>A result with <see cref="ImageProcessState.Unsupported"/> state.</returns>
    public static ImageStreamCompressResult NotSupportedMimeType(string mimType)
    {
        return NotSupported($"The given MIME type {mimType} is not supported.");
    }

    /// <summary>Creates an <see cref="ImageProcessState.Failed"/> result with a custom error message.</summary>
    /// <param name="error">A description of why compression failed.</param>
    /// <returns>A result with <see cref="ImageProcessState.Failed"/> state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static ImageStreamCompressResult Failed(string error = FailedError)
    {
        return new() { State = ImageProcessState.Failed, Error = Argument.IsNotNull(error) };
    }

    /// <summary>Creates a successful <see cref="ImageProcessState.Done"/> result.</summary>
    /// <param name="content">A readable stream containing the compressed image bytes.</param>
    /// <returns>A result with <see cref="ImageProcessState.Done"/> state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public static ImageStreamCompressResult Done(Stream content)
    {
        return new() { State = ImageProcessState.Done, Result = Argument.IsNotNull(content) };
    }
}
