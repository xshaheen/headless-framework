// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

internal static class TopicNormalizer
{
    public static string NormalizeForAws(this string origin)
    {
        if (origin.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(origin) + " character string length must between 1~256!");
        return origin.Replace(".", "-").Replace(":", "_");
    }
}
