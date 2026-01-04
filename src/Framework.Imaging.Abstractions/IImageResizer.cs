// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Imaging;

public interface IImageResizer
{
    Task<ImageStreamResizeResult> ResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}
