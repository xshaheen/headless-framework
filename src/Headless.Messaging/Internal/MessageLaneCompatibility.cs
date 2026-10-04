// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>Checked mappings at the stable persisted and wire compatibility boundaries.</summary>
internal static class MessageLaneCompatibility
{
    internal const short BusPersistedValue = 0;
    internal const short QueuePersistedValue = 1;
    internal const string BusWireValue = "Bus";
    internal const string QueueWireValue = "Queue";

    internal static MessageLane FromPersistedValue(short value) =>
        value switch
        {
            BusPersistedValue => MessageLane.Bus,
            QueuePersistedValue => MessageLane.Queue,
            _ => throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Unsupported persisted messaging lane value '{value}'.")
            ),
        };

    internal static short ToPersistedValue(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => BusPersistedValue,
            MessageLane.Queue => QueuePersistedValue,
            _ => throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Unsupported messaging lane value '{(short)lane}'.")
            ),
        };

    internal static MessageLane FromWireValue(string value) =>
        value switch
        {
            BusWireValue => MessageLane.Bus,
            QueueWireValue => MessageLane.Queue,
            _ => throw new InvalidOperationException($"Unsupported messaging lane header value '{value}'."),
        };

    internal static string ToWireValue(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => BusWireValue,
            MessageLane.Queue => QueueWireValue,
            _ => throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Unsupported messaging lane value '{(short)lane}'.")
            ),
        };
}
