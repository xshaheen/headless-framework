// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Imaging;

/// <summary>The result of an image inspection.</summary>
/// <remarks>
/// Check <see cref="ImageProcessResult{T}.IsDone"/> before accessing <see cref="ImageProcessResult{T}.Result"/>.
/// </remarks>
[PublicAPI]
public sealed class ImageInspectResult : ImageProcessResult<ImageInfo>
{
    private ImageInspectResult() { }

    /// <summary>Creates an <see cref="ImageProcessState.Unsupported"/> result indicating the stream could not be read.</summary>
    /// <returns>A result with <see cref="ImageProcessState.Unsupported"/> state.</returns>
    public static ImageInspectResult CannotRead()
    {
        return NotSupported(CannotReadError);
    }

    /// <summary>Creates an <see cref="ImageProcessState.Unsupported"/> result with a custom error message.</summary>
    /// <param name="error">A description of why the format is not supported.</param>
    /// <returns>A result with <see cref="ImageProcessState.Unsupported"/> state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static ImageInspectResult NotSupported(string error = UnsupportedError)
    {
        return new() { State = ImageProcessState.Unsupported, Error = Argument.IsNotNull(error) };
    }

    /// <summary>Creates an <see cref="ImageProcessState.Failed"/> result with a custom error message.</summary>
    /// <param name="error">A description of why inspection failed.</param>
    /// <returns>A result with <see cref="ImageProcessState.Failed"/> state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static ImageInspectResult Failed(string error = FailedError)
    {
        return new() { State = ImageProcessState.Failed, Error = Argument.IsNotNull(error) };
    }

    /// <summary>Creates a successful <see cref="ImageProcessState.Done"/> result.</summary>
    /// <param name="info">What the inspection found.</param>
    /// <returns>A result with <see cref="ImageProcessState.Done"/> state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is <see langword="null"/>.</exception>
    public static ImageInspectResult Done(ImageInfo info)
    {
        return new() { State = ImageProcessState.Done, Result = Argument.IsNotNull(info) };
    }
}
