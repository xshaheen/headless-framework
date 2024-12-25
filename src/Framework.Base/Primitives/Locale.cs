// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

[PublicAPI]
public sealed class Locale : Dictionary<string, Dictionary<string, string>>
{
    public Locale() { }

    public Locale(IDictionary<string, Dictionary<string, string>> d)
        : base(d) { }
}
