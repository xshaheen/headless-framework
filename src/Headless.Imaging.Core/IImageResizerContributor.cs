// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Imaging;

namespace Headless.Imaging;

public interface IImageResizerContributor
{
    Task<ImageStreamResizeResult> TryResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}
