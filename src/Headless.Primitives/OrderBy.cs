// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A single sort instruction: the property to sort by and the direction.</summary>
/// <param name="Property">The name of the property to sort by.</param>
/// <param name="Ascending"><see langword="true"/> (the default) to sort ascending; <see langword="false"/> to sort descending.</param>
[PublicAPI]
public sealed record OrderBy(string Property, bool Ascending = true);
