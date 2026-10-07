// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>A document number: the counter value, the period it was taken in, and the formatted text.</summary>
/// <param name="Value">The counter value.</param>
/// <param name="Partition">
/// The counter partition the policy's reset chose (such as <c>2026</c>, <c>2026-10</c>, or <c>FY2026</c>), or
/// <see langword="null" /> when the policy never resets.
/// </param>
/// <param name="IssuedOn">The date the number was taken, in the policy's time zone.</param>
/// <param name="Text">The number formatted with the policy's template, or the bare value when it has none.</param>
[PublicAPI]
public sealed record SequenceNumber(long Value, string? Partition, DateOnly IssuedOn, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}
