// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Compliance.Redaction;

namespace Headless.Logging;

/// <summary>
/// A redactor that keeps a short readable prefix and masks the rest, so a log can tell device tokens or keys apart by
/// their first characters without carrying them whole.
/// </summary>
/// <remarks>
/// A value no longer than the prefix is masked completely, because showing all of a short value would hide nothing.
/// Unlike an HMAC pseudonym, the prefix is the real value's start: map it only to identifiers whose first characters are
/// not themselves sensitive. Register it for a classification with
/// <c>redaction.SetRedactor&lt;PrefixMaskRedactor&gt;(classifications)</c>, which keeps 8 characters, or construct it with
/// another length.
/// </remarks>
/// <param name="visibleLength">How many leading characters stay readable.</param>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="visibleLength"/> is negative.</exception>
[PublicAPI]
public sealed class PrefixMaskRedactor(int visibleLength) : Redactor
{
    /// <summary>The text that stands for the hidden characters.</summary>
    public const string Mask = "***";

    private readonly int _visibleLength = Argument.IsPositiveOrZero(visibleLength);

    /// <summary>Creates a redactor that keeps the first 8 characters. Dependency injection uses this constructor.</summary>
    public PrefixMaskRedactor()
        : this(visibleLength: 8) { }

    /// <inheritdoc />
    public override int GetRedactedLength(ReadOnlySpan<char> input) => _Masked(input).Length;

    /// <inheritdoc />
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        var masked = _Masked(source);
        masked.AsSpan().CopyTo(destination);

        return masked.Length;
    }

    private string _Masked(ReadOnlySpan<char> input) => input.ToString().MaskEnd(_visibleLength, Mask);
}
