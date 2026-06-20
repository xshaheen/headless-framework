// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// A nested dictionary of localized strings, keyed first by locale and then by string key
/// (<c>locale → (key → value)</c>).
/// </summary>
[PublicAPI]
public sealed class Locales : Dictionary<string, Dictionary<string, string>>
{
    /// <summary>Initializes an empty <see cref="Locales"/> dictionary.</summary>
    public Locales() { }

    /// <summary>Initializes a <see cref="Locales"/> dictionary seeded from an existing dictionary.</summary>
    /// <param name="d">The dictionary whose entries are copied into the new instance.</param>
    public Locales(IDictionary<string, Dictionary<string, string>> d)
        : base(d) { }
}
