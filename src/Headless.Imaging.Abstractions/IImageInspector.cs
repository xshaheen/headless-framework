// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging;

/// <summary>Provider-agnostic contract for reading what an image is without decoding its pixels.</summary>
/// <remarks>
/// <para>
/// Use it to validate an upload by its bytes rather than its claimed content type or file name, to record the
/// dimensions an image displays at, or to choose how to process it.
/// </para>
/// <para>
/// The default implementation delegates to a chain of <c>IImageInspectorContributor</c> instances registered in the DI
/// container, tried in reverse registration order; the first one that does not return
/// <see cref="ImageProcessState.Unsupported"/> wins. A contributor reports a format as unsupported when its resizer
/// and compressor cannot process it, so a successful inspection means the pipeline accepts the format.
/// </para>
/// </remarks>
[PublicAPI]
public interface IImageInspector
{
    /// <summary>Reads the format, displayed size, frame count, and transparency of the image in <paramref name="stream"/>.</summary>
    /// <param name="stream">
    /// A readable stream containing the image. Non-seekable streams are buffered into memory automatically.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    /// <returns>
    /// An <see cref="ImageInspectResult"/> describing the image, or why it could not be inspected. Inspection reads
    /// only the headers, so <see cref="ImageProcessState.Done"/> does not prove that every pixel decodes.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    Task<ImageInspectResult> InspectAsync(Stream stream, CancellationToken cancellationToken = default);
}
