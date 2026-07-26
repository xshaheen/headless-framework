// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        const string fifoSuffix = ".fifo";
        var isFifo = value.EndsWith(fifoSuffix, StringComparison.Ordinal);
        var core = isFifo ? value[..^fifoSuffix.Length] : value;
        core = core.Replace('.', '-').Replace(':', '_');

        var suffix = isFifo ? fifoSuffix : string.Empty;
        var qualified = $"{lane}-{core}{suffix}";
        if (qualified.Length <= maxLength)
        {
            return qualified;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(qualified)))[..12];
        var availableCoreLength = maxLength - lane.Length - hash.Length - suffix.Length - 2;
        var truncatedCore = core[..availableCoreLength].TrimEnd('-', '_');
        return $"{lane}-{truncatedCore}-{hash}{suffix}";
    }
}
