// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging;

public interface IImageResizer
{
    Task<ImageStreamResizeResult> ResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}
