// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging;

/// <summary>Provider-agnostic contract for compressing images.</summary>
/// <remarks>
/// The default implementation delegates work to a chain of <c>IImageCompressorContributor</c>
/// instances registered in the DI container. Contributors are tried in reverse registration order;
/// the first one that does not return <see cref="ImageProcessState.Unsupported"/> wins.
/// </remarks>
[PublicAPI]
public interface IImageCompressor
{
    /// <summary>Compresses the image in <paramref name="stream"/> according to <paramref name="args"/>.</summary>
    /// <param name="stream">
    /// A readable stream containing the source image. Non-seekable streams are buffered into memory
    /// automatically before processing.
    /// </param>
    /// <param name="args">Parameters that control the compression, such as the expected MIME type.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    /// <returns>
    /// An <see cref="ImageStreamCompressResult"/> describing the outcome. Per-image problems are
    /// surfaced through the result rather than thrown; inspect
    /// <see cref="ImageProcessResult{T}.IsDone"/> and <see cref="ImageProcessResult{T}.State"/>
    /// to react to failures.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    Task<ImageStreamCompressResult> CompressAsync(
        Stream stream,
        ImageCompressArgs args,
        CancellationToken cancellationToken = default
    );
}
