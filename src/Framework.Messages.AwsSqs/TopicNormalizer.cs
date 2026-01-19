// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Messages;

internal static class TopicNormalizer
{
    public static string NormalizeForAws(this string origin)
    {
        Argument.IsGreaterThan(origin.Length, 256);

        return origin.Replace('.', '-').Replace(':', '_');
    }
}
