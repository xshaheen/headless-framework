// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Imaging;

/// <summary>Specifies the algorithm used to fit an image into a target bounding box.</summary>
public enum ImageResizeMode
{
    /// <summary>Default value. Doesn't apply any resizing.</summary>
    None = 0,

    /// <summary>Stretches the resized image to fit the bounds of its container.</summary>
    Stretch = 1,

    /// <summary>
    /// Pads the image to fit the bound of the container without resizing the original source.
    /// When downscaling, performs the same functionality as <see cref="Pad" />
    /// </summary>
    BoxPad = 2,

    /// <summary>
    /// Resizes the image until the shortest side reaches the set given dimension.
    /// Upscaling is disabled in this mode, and the original image will be returned
    /// if attempted.
    /// </summary>
    Min = 3,

    /// <summary>Constrains the resized image to fit the bounds of its container, maintaining the original aspect ratio.</summary>
    Max = 4,

    /// <summary>Crops the resized image to fit the bounds of its container.</summary>
    Crop = 5,

    /// <summary>
    /// Pads the resized image to fit the bounds of its container.
    /// If only one dimension is passed, will maintain the original aspect ratio.
    /// </summary>
    Pad = 6,

    /// <summary>
    /// Sentinel value meaning "use the pipeline default". Replaced at runtime by
    /// <see cref="ImageResizeArgs.ChangeDefaultResizeMode"/> before the operation executes.
    /// Do not pass this value to a contributor directly.
    /// </summary>
    Default = 7,
}
