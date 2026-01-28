// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging;

namespace Headless.Imaging;

public interface IImageCompressor
{
    Task<ImageStreamCompressResult> CompressAsync(
        Stream stream,
        ImageCompressArgs args,
        CancellationToken cancellationToken = default
    );
}
