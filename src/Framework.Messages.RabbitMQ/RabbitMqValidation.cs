// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Framework.Checks;

namespace Framework.Messages;

internal static partial class RabbitMqValidation
{
    // RabbitMQ naming rules: alphanumeric, dash, underscore, period
    // Max length 255 chars
    [GeneratedRegex(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled)]
    private static partial Regex _NamePattern();

    private const int MaxNameLength = 255;

    internal static void ValidateQueueName(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, MaxNameLength, "Queue name must not exceed 255 characters");
        Argument.Matches(
            name,
            _NamePattern(),
            "Queue name must contain only alphanumeric characters, dashes, underscores, and periods"
        );
    }

    internal static void ValidateExchangeName(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, MaxNameLength, "Exchange name must not exceed 255 characters");
        Argument.Matches(
            name,
            _NamePattern(),
            "Exchange name must contain only alphanumeric characters, dashes, underscores, and periods"
        );
    }

    internal static void ValidateTopicName(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, MaxNameLength, "Topic name must not exceed 255 characters");
        // Topics can contain wildcards (* and #) in addition to regular name chars
        // But for safety, we validate the same as queue/exchange names for now
        Argument.Matches(
            name,
            _NamePattern(),
            "Topic name must contain only alphanumeric characters, dashes, underscores, and periods"
        );
    }
}
