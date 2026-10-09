// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging;

/// <summary>Selects whether JPEG and AVIF output store colour (chroma) at half resolution (4:2:0) or in full (4:4:4).</summary>
[PublicAPI]
public enum NetVipsChromaSubsampling
{
    /// <summary>
    /// Subsamples below quality 90 and keeps full colour at 90 and above, where the eye can see the difference on
    /// sharp coloured edges such as text and logos. libvips' default.
    /// </summary>
    Auto = 0,

    /// <summary>Always subsamples: the smallest files, with soft edges on saturated colour.</summary>
    On = 1,

    /// <summary>Never subsamples: crisp colour edges for screenshots, text, and logos, at a larger size.</summary>
    Off = 2,
}
