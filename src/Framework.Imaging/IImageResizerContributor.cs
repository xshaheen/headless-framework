// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Imaging.Contracts;

namespace Framework.Imaging;

public interface IImageResizerContributor
{
    Task<ImageStreamResizeResult> TryResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}
