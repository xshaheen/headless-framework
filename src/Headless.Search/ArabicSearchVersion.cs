// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Search;

/// <summary>
/// A frozen version of the Arabic search rules. Stored search keys are built by one version, so a query must normalize
/// its term with the same version; a rule change ships as a new version and never edits a released one.
/// </summary>
[PublicAPI]
public enum ArabicSearchVersion
{
    /// <summary>
    /// The first rule set: hamza seats, alef, yeh, heh, and Persian, Urdu, and Kurdish letter variants folded; tashkeel,
    /// tatweel, and Quranic marks deleted; Arabic presentation forms folded; Latin letters folded to ASCII lowercase;
    /// Arabic-Indic digits to ASCII; everything else a word separator.
    /// </summary>
    V1 = 1,
}
