// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging;

/// <summary>
/// Extension point for adding a resize backend to the imaging pipeline.
/// Implement this interface and register it with DI to plug in a new resizer.
/// </summary>
/// <remarks>
/// Contributors are tried in reverse registration order. A contributor signals that it cannot
/// handle the image by returning a result with <see cref="ImageProcessState.Unsupported"/>,
/// which causes the pipeline to fall through to the next contributor. The last registered
/// contributor whose result is not <c>Unsupported</c> determines the final outcome.
/// </remarks>
public interface IImageResizerContributor
{
    /// <summary>
    /// Attempts to resize the image in <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">
    /// A readable, seekable stream containing the source image. The caller rewinds the stream
    /// to the beginning after each contributor call so subsequent contributors see the full input.
    /// </param>
    /// <param name="args">Parameters that control the resize, including dimensions, mode, and MIME type hint.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    /// <returns>
    /// An <see cref="ImageStreamResizeResult"/> with <see cref="ImageProcessState.Unsupported"/>
    /// when this contributor cannot handle the format, or <see cref="ImageProcessState.Done"/> on success.
    /// </returns>
    Task<ImageStreamResizeResult> TryResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}
