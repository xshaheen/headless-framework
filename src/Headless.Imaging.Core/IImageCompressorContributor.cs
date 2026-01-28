// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging;

namespace Headless.Imaging;

public interface IImageCompressorContributor
{
    Task<ImageStreamCompressResult> TryCompressAsync(
        Stream stream,
        ImageCompressArgs args,
        CancellationToken cancellationToken = default
    );
}
