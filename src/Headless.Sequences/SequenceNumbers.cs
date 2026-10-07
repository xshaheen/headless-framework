// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

internal static class SequenceNumbers
{
    public static SequenceNumber Create(SequencePolicy policy, long value, string? partition, DateOnly issuedOn)
    {
        var text = policy.Format is { } format
            ? SequenceNumberFormat.Get(format).Format(policy, value, issuedOn)
            : value.ToString(CultureInfo.InvariantCulture);

        return new SequenceNumber(value, partition, issuedOn, text);
    }
}
