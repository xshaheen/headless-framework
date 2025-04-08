// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Imaging;

public interface IImageResizerContributor
{
    Task<ImageStreamResizeResult> TryResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}
