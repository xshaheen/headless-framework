// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging;

/// <summary>
/// Extension point for adding an inspection backend to the imaging pipeline.
/// Implement this interface and register it with DI to plug in a new inspector.
/// </summary>
/// <remarks>
/// Contributors are tried in reverse registration order. A contributor signals that it cannot handle the image by
/// returning a result with <see cref="ImageProcessState.Unsupported"/>, which causes the pipeline to fall through to
/// the next contributor. Report a format as unsupported when the same provider's resizer and compressor would refuse
/// it, so inspection and processing agree.
/// </remarks>
[PublicAPI]
public interface IImageInspectorContributor
{
    /// <summary>Attempts to inspect the image in <paramref name="stream"/>.</summary>
    /// <param name="stream">
    /// A readable, seekable stream containing the image. The caller rewinds the stream to the beginning after each
    /// contributor call so subsequent contributors see the full input.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    /// <returns>
    /// An <see cref="ImageInspectResult"/> with <see cref="ImageProcessState.Unsupported"/> when this contributor
    /// cannot handle the format, or <see cref="ImageProcessState.Done"/> with what it found.
    /// </returns>
    Task<ImageInspectResult> TryInspectAsync(Stream stream, CancellationToken cancellationToken = default);
}
