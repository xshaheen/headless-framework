// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging;

/// <summary>Selects the region <see cref="ImageResizeMode.Crop" /> keeps when the image is cut to the target box.</summary>
[PublicAPI]
public enum NetVipsCropFocus
{
    /// <summary>Keeps the center of the image.</summary>
    Center = 0,

    /// <summary>
    /// Keeps the region most likely to draw the eye, scored from skin tones, saturated colour, and edges. Suits
    /// photographs of people and products.
    /// </summary>
    Attention = 1,

    /// <summary>Keeps the region with the most detail, measured as Shannon entropy.</summary>
    Entropy = 2,
}
