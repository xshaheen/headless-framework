// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;

namespace Headless.Messaging.Aws;

internal static class AwsRoutingAffinity
{
    internal static readonly MessagingRoutingAffinityMapping Mapping = new(
        AwsMessagingHeaders.MessageGroupId,
        maximumKeyLength: 128,
        printableAsciiOnly: true
    );
}
