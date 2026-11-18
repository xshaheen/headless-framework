// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>One saved row of a dependency key: the row as it was before the write and as it is after.</summary>
/// <param name="Key">The dependency key the row belongs to.</param>
/// <param name="Old">The row before the write, or <see langword="null" /> for an inserted row.</param>
/// <param name="New">The row after the write, or <see langword="null" /> for a deleted row.</param>
internal sealed record LiveRowChange(string Key, object? Old, object? New);
