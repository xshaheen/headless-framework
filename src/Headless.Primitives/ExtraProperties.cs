// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Primitives.Internal;

namespace Headless.Primitives;

/// <summary>
/// An ordinal-keyed string-to-object dictionary for storing arbitrary extra properties on a model
/// (for example, dynamic or extensible fields not represented by first-class properties).
/// </summary>
[PublicAPI]
public sealed class ExtraProperties : Dictionary<string, object?>
{
    /// <summary>Initializes an empty <see cref="ExtraProperties"/> using ordinal key comparison.</summary>
    public ExtraProperties()
        : base(StringComparer.Ordinal) { }

    /// <summary>Initializes an <see cref="ExtraProperties"/> seeded from an existing dictionary, using ordinal key comparison.</summary>
    /// <param name="dictionary">The dictionary whose entries are copied into the new instance.</param>
    public ExtraProperties(IDictionary<string, object?> dictionary)
        : base(dictionary, StringComparer.Ordinal) { }
}
