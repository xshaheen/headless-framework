// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging;

namespace Headless.Imaging;

public interface IImageResizer
{
    Task<ImageStreamResizeResult> ResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}
