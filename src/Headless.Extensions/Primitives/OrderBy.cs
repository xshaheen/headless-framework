// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A single sort instruction: the property to sort by and the direction.</summary>
/// <param name="Property">The name of the property to sort by.</param>
/// <param name="Ascending"><see langword="true"/> to sort ascending; <see langword="false"/> (the default) to sort descending.</param>
public sealed record OrderBy(string Property, bool Ascending = false);
