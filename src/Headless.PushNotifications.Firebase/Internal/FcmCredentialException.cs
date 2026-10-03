// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase.Internal;

/// <summary>
/// The service account credentials could not be loaded into a Firebase app. Marks the failure so it classifies as
/// <see cref="FcmFailureKind.Authentication"/>: the loader throws general exception types, such as
/// <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/>, that would otherwise read as a
/// transport or payload failure.
/// </summary>
#pragma warning disable CA1064 // False positive: the sender turns it into a send result, so it never leaves this assembly.
internal sealed class FcmCredentialException(Exception innerException)
    : Exception(
        "The Firebase service account credentials could not be loaded: " + innerException.Message,
        innerException
    );
#pragma warning restore CA1064
