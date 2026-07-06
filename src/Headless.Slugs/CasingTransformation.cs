// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Slugs;

/// <summary>Controls whether and how alphabetic characters in a slug are case-folded.</summary>
/// <remarks>
/// The backing values are part of the public contract and must remain stable across versions; assign new members
/// explicit values rather than reordering existing ones.
/// </remarks>
public enum CasingTransformation
{
    /// <summary>Leaves the original casing of each character unchanged.</summary>
    PreserveCase = 0,

    /// <summary>Folds every character to lower case. Uses the invariant culture unless <c>Culture</c> is set on <c>SlugOptions</c>.</summary>
    ToLowerCase = 1,

    /// <summary>Folds every character to upper case. Uses the invariant culture unless <c>Culture</c> is set on <c>SlugOptions</c>.</summary>
    ToUpperCase = 2,
}
