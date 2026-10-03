// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Primitives.Internal;

namespace Headless.Primitives;

/// <summary>Marks a type that carries a bag of arbitrary <see cref="ExtraProperties"/>.</summary>
[PublicAPI]
public interface IHasExtraProperties
{
    /// <summary>The bag of arbitrary extra properties associated with this instance.</summary>
    ExtraProperties ExtraProperties { get; }
}
