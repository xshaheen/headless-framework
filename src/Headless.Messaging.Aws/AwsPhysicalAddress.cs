// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Aws;

/// <summary>Single authority for lane-qualified SNS topics and SQS queues.</summary>
internal static class AwsPhysicalAddress
{
    public static string BusTopic(string logicalName)
    {
        return _Qualify("bus", logicalName, maxLength: 256);
    }

    public static string BusGroupQueue(string subscriberGroup)
    {
        return _Qualify("bus", subscriberGroup, maxLength: 80);
    }

    public static string QueueDestination(string logicalName)
    {
        return _Qualify("queue", logicalName, maxLength: 80);
    }

    private static string _Qualify(string lane, string value, int maxLength)
    {
        Argument.IsNotNullOrWhiteSpace(value);

        const string fifoSuffix = ".fifo";
        var isFifo = value.IsAwsFifoName();
        var core = isFifo ? value[..^fifoSuffix.Length] : value;
        var normalizedCore = core.Replace('.', '-').Replace(':', '_');

        var suffix = isFifo ? fifoSuffix : string.Empty;
        var qualified = $"{lane}-{normalizedCore}{suffix}";
        var normalizationChangedIdentity = !string.Equals(core, normalizedCore, StringComparison.Ordinal);
        if (!normalizationChangedIdentity && qualified.Length <= maxLength)
        {
            return qualified;
        }

        // The hash is derived from the pre-normalized identity so distinct logical names cannot collapse
        // onto the same broker resource after replacing AWS-incompatible characters.
        var hash = $"{lane}-{core}{suffix}".ToSha256()[..12];
        var availableCoreLength = maxLength - lane.Length - hash.Length - suffix.Length - 2;
        var boundedCore =
            normalizedCore.Length > availableCoreLength
                ? normalizedCore[..availableCoreLength].TrimEnd('-', '_')
                : normalizedCore;
        return $"{lane}-{boundedCore}-{hash}{suffix}";
    }
}
