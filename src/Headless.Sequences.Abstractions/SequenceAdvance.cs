// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sequences;

/// <summary>The result of moving a reported counter forward.</summary>
/// <param name="Status">Whether the value was the next one, skipped ahead, or stale.</param>
/// <param name="Previous">
/// The highest value accepted before this call, or <see langword="null" /> when the counter had accepted none.
/// </param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SequenceAdvance(SequenceAdvanceStatus Status, long? Previous)
{
    /// <summary>Gets whether the counter moved to the value (<see cref="SequenceAdvanceStatus.Next" /> or <see cref="SequenceAdvanceStatus.Skipped" />).</summary>
    public bool IsAccepted => Status != SequenceAdvanceStatus.Stale;
}
