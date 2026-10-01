// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Aws;

/// <summary>Single authority for lane-qualified SNS topics and SQS queues.</summary>
internal static class AwsPhysicalAddress
{
    private const string _BusQueuePrefix = "bus-";
    private const string _FifoSuffix = ".fifo";
    private const int _SqsQueueNameMaxLength = 80;

    // SQS queue names allow letters, digits, '-', and '_', and the ".fifo" suffix counts toward the 80-character limit.
    private static readonly BusNameRules _BusQueueRules = new(
        maxLength: _SqsQueueNameMaxLength - _BusQueuePrefix.Length,
        isAllowed: static c => c is '-' or '_'
    );

    private static readonly BusNameRules _BusFifoQueueRules = new(
        maxLength: _SqsQueueNameMaxLength - _BusQueuePrefix.Length - _FifoSuffix.Length,
        isAllowed: static c => c is '-' or '_'
    );

    public static string BusTopic(string logicalName)
    {
        return _Qualify("bus", logicalName, maxLength: 256);
    }

    /// <summary>Returns the SQS queue a Bus consumer identity subscribes to the topics of its messages through.</summary>
    /// <remarks>
    /// An identity ending in <c>.fifo</c> gets a FIFO queue, which is the only kind a FIFO topic delivers to.
    /// </remarks>
    public static string BusSubscriptionQueue(string identity)
    {
        Argument.IsNotNullOrWhiteSpace(identity);

        return identity.IsAwsFifoName()
            ? _BusQueuePrefix + BusNameBuilder.Build(identity[..^_FifoSuffix.Length], _BusFifoQueueRules) + _FifoSuffix
            : _BusQueuePrefix + BusNameBuilder.Build(identity, _BusQueueRules);
    }

    public static string QueueDestination(string logicalName)
    {
        return _Qualify("queue", logicalName, maxLength: 80);
    }

    private static string _Qualify(string lane, string value, int maxLength)
    {
        Argument.IsNotNullOrWhiteSpace(value);

        var isFifo = value.IsAwsFifoName();
        var core = isFifo ? value[..^_FifoSuffix.Length] : value;
        var normalizedCore = core.Replace('.', '-').Replace(':', '_');

        var suffix = isFifo ? _FifoSuffix : string.Empty;
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
