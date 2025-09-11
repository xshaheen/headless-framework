// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

[PublicAPI]
public sealed class Locales : Dictionary<string, Dictionary<string, string>>
{
    public Locales() { }

    public Locales(IDictionary<string, Dictionary<string, string>> d)
        : base(d) { }
}
