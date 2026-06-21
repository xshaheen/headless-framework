// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Slugs;

/// <summary>Controls whether and how alphabetic characters in a slug are case-folded.</summary>
public enum CasingTransformation
{
    /// <summary>Leaves the original casing of each character unchanged.</summary>
    PreserveCase,

    /// <summary>Folds every character to lower case. Uses the invariant culture unless <c>Culture</c> is set on <c>SlugOptions</c>.</summary>
    ToLowerCase,

    /// <summary>Folds every character to upper case. Uses the invariant culture unless <c>Culture</c> is set on <c>SlugOptions</c>.</summary>
    ToUpperCase,
}
