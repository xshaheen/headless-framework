// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.Logging.Internal;

/// <summary>Decides how a log value is scanned, so every channel masks the same values the same way.</summary>
internal static class ValueMasker
{
    /// <summary>
    /// Returns <paramref name="value"/> itself when its text has no match; otherwise its masked text. A non-string value is
    /// scanned in its invariant text form.
    /// </summary>
    public static object? Mask<TState>(object? value, Func<string, TState, string> mask, TState state)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                var masked = mask(text, state);
                return string.Equals(masked, text, StringComparison.Ordinal) ? text : masked;
        }

        // These render to text no value shape can hide in, so formatting them would only cost an allocation.
        // Integers are still scanned: a 14-digit id held as a long renders as that id.
        if (value is bool or Guid or Enum or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan)
        {
            return value;
        }

        // A collection formats as its type name ("System.String[]"), which no pattern matches, so the elements would
        // reach the providers unmasked. Scan each element instead; raw bytes carry no text to scan.
        if (value is System.Collections.IEnumerable items and not byte[])
        {
            return _MaskElements(items, value, mask, state);
        }

        var formatted = Convert.ToString(value, CultureInfo.InvariantCulture);

        if (formatted is null)
        {
            return value;
        }

        var scrubbed = mask(formatted, state);

        return string.Equals(scrubbed, formatted, StringComparison.Ordinal) ? value : scrubbed;
    }

    /// <summary>Masks each element, returning <paramref name="original"/> unless some element changed.</summary>
    private static object _MaskElements<TState>(
        System.Collections.IEnumerable items,
        object original,
        Func<string, TState, string> mask,
        TState state
    )
    {
        var masked = new List<object?>();
        var changed = false;

        foreach (var item in items)
        {
            var maskedItem = Mask(item, mask, state);
            changed |= !ReferenceEquals(maskedItem, item);
            masked.Add(maskedItem);
        }

        return changed ? masked.ToArray() : original;
    }
}
