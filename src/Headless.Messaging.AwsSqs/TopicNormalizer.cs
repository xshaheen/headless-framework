// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.AwsSqs;

internal static class TopicNormalizer
{
    public static string NormalizeForAws(this string origin)
    {
        Argument.IsNotNullOrWhiteSpace(origin);
        Argument.IsLessThanOrEqualTo(origin.Length, 256, "AWS SNS topic names must be 256 characters or less");

        return origin.Replace('.', '-').Replace(':', '_');
    }
}
