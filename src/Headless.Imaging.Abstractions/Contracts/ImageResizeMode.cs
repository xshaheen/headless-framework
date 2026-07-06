// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Imaging;

/// <summary>Specifies the algorithm used to fit an image into a target bounding box.</summary>
public enum ImageResizeMode
{
    /// <summary>
    /// Default (unset) value. Sentinel meaning "use the pipeline default": replaced at runtime by
    /// <c>ImagingOptions.DefaultResizeMode</c> via <see cref="ImageResizeArgs.ChangeDefaultResizeMode"/>
    /// before the operation executes. Do not pass this value to a contributor directly.
    /// </summary>
    Default = 0,

    /// <summary>Applies no resizing; the source image is returned unchanged.</summary>
    None = 1,

    /// <summary>Stretches the resized image to fit the bounds of its container.</summary>
    Stretch = 2,

    /// <summary>
    /// Pads the image to fit the bound of the container without resizing the original source.
    /// When downscaling, performs the same functionality as <see cref="Pad" />
    /// </summary>
    BoxPad = 3,

    /// <summary>
    /// Resizes the image until the shortest side reaches the set given dimension.
    /// Upscaling is disabled in this mode, and the original image will be returned
    /// if attempted.
    /// </summary>
    Min = 4,

    /// <summary>Constrains the resized image to fit the bounds of its container, maintaining the original aspect ratio.</summary>
    Max = 5,

    /// <summary>Crops the resized image to fit the bounds of its container.</summary>
    Crop = 6,

    /// <summary>
    /// Pads the resized image to fit the bounds of its container.
    /// If only one dimension is passed, will maintain the original aspect ratio.
    /// </summary>
    Pad = 7,
}
