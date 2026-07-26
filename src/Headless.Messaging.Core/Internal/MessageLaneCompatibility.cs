// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Converts the persisted and wire-compatible <see cref="IntentType"/> representation at Core boundaries.
/// </summary>
internal static class MessageLaneCompatibility
{
    public static MessageLane ToLane(IntentType intentType) =>
        intentType switch
        {
            IntentType.Bus => MessageLane.Bus,
            IntentType.Queue => MessageLane.Queue,
            _ => throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unsupported persisted messaging intent value '{(short)intentType}'."
                )
            ),
        };

    public static IntentType ToIntentType(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => IntentType.Bus,
            MessageLane.Queue => IntentType.Queue,
            _ => throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Unsupported messaging lane value '{(short)lane}'.")
            ),
        };
}
