// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>A validated notification ready to send: its JSON payload and its APNs request headers.</summary>
internal sealed record ApnsPreparedNotification(byte[] Payload, ApnsRequestHeaders Headers);
