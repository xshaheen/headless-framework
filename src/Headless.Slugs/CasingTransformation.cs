// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Slugs;

/// <summary>Specifies how alphabetic characters in a slug are case-folded.</summary>
/// <remarks>
/// The backing values are part of the public contract and must remain stable across versions. Assign new members
/// explicit values rather than reordering existing members.
/// </remarks>
public enum CasingTransformation
{
    /// <summary>Preserves the original casing of each character.</summary>
    PreserveCase = 0,

    /// <summary>Converts each character to lowercase. Uses invariant culture unless <see cref="SlugOptions.Culture"/> is set.</summary>
    ToLowerCase = 1,

    /// <summary>Converts each character to uppercase. Uses invariant culture unless <see cref="SlugOptions.Culture"/> is set.</summary>
    ToUpperCase = 2,
}
