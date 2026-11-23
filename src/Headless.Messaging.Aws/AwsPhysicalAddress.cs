// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Aws;

/// <summary>Single authority for lane-qualified SNS topics and SQS queues.</summary>
/// <remarks>
/// With a host isolation prefix (<c>Headless:Prefix</c>) every topic and queue starts with <c>{prefix}_</c>, so
/// environments that share one account and region never share either. A prefix holds no <c>_</c>, so it ends at the
/// first underscore and two different prefixes never produce one name. The prefix counts toward each name's length
/// limit, and a name it pushes past the limit falls back to the readable-prefix-and-hash form, as a long name does
/// without one. An empty prefix leaves every name exactly as it is without one.
/// </remarks>
internal static class AwsPhysicalAddress
{
    private const string _BusQueuePrefix = "bus-";
    private const string _FifoSuffix = ".fifo";
    private const int _SqsQueueNameMaxLength = 80;

    // SQS queue names allow letters, digits, '-', and '_', and the ".fifo" suffix counts toward the 80-character limit.
    private static readonly BusNameRules _BusQueueRules = _BusRules(_SqsQueueNameMaxLength - _BusQueuePrefix.Length);

    private static readonly BusNameRules _BusFifoQueueRules = _BusRules(
        _SqsQueueNameMaxLength - _BusQueuePrefix.Length - _FifoSuffix.Length
    );

    public static string BusTopic(string hostPrefix, string logicalName)
    {
        return _Qualify(hostPrefix, "bus", logicalName, maxLength: 256);
    }

    /// <summary>Returns the SQS queue a Bus consumer identity subscribes to the topics of its messages through.</summary>
    /// <remarks>
    /// An identity ending in <c>.fifo</c> gets a FIFO queue, which is the only kind a FIFO topic delivers to.
    /// </remarks>
    public static string BusSubscriptionQueue(string hostPrefix, string identity)
    {
        Argument.IsNotNull(hostPrefix);
        Argument.IsNotNullOrWhiteSpace(identity);

        if (hostPrefix.Length == 0)
        {
            return identity.IsAwsFifoName()
                ? _BusQueuePrefix
                    + BusNameBuilder.Build(identity[..^_FifoSuffix.Length], _BusFifoQueueRules)
                    + _FifoSuffix
                : _BusQueuePrefix + BusNameBuilder.Build(identity, _BusQueueRules);
        }

        // The ".fifo" suffix stays last, where SQS requires it.
        var queuePrefix = $"{hostPrefix}_{_BusQueuePrefix}";
        return identity.IsAwsFifoName()
            ? queuePrefix
                + BusNameBuilder.Build(
                    identity[..^_FifoSuffix.Length],
                    _BusRules(_SqsQueueNameMaxLength - queuePrefix.Length - _FifoSuffix.Length)
                )
                + _FifoSuffix
            : queuePrefix + BusNameBuilder.Build(identity, _BusRules(_SqsQueueNameMaxLength - queuePrefix.Length));
    }

    public static string QueueDestination(string hostPrefix, string logicalName)
    {
        return _Qualify(hostPrefix, "queue", logicalName, maxLength: 80);
    }

    private static BusNameRules _BusRules(int maxLength) => new(maxLength, isAllowed: static c => c is '-' or '_');

    private static string _Qualify(string hostPrefix, string laneName, string value, int maxLength)
    {
        Argument.IsNotNull(hostPrefix);
        Argument.IsNotNullOrWhiteSpace(value);

        var lane = hostPrefix.Length == 0 ? laneName : $"{hostPrefix}_{laneName}";

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
