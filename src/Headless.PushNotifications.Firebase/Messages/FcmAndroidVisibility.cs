// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

/// <summary>How an Android notification shows on a locked screen.</summary>
[PublicAPI]
public enum FcmAndroidVisibility
{
    /// <summary>Shows only a redacted version on a secure lock screen. Android's default.</summary>
    Private = 0,

    /// <summary>Shows in full on every lock screen.</summary>
    Public = 1,

    /// <summary>Hidden on a secure lock screen.</summary>
    Secret = 2,
}
