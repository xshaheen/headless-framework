// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Describes the delivery outcome for a single device token.
/// </summary>
/// <remarks>
/// Exactly one of three states applies, distinguished by <see cref="Status"/>: a successful send carries
/// a non-null <see cref="MessageId"/>; a failed send carries a non-null <see cref="FailureError"/>; an
/// unregistered token carries neither and signals that the token should be removed from your store. Note
/// that for an unregistered token both <see cref="IsSucceeded"/> and <see cref="IsFailed"/> return
/// <see langword="false"/>.
/// </remarks>
public sealed record PushNotificationResponse
{
    private PushNotificationResponse() { }

    /// <summary>The device registration token this response refers to.</summary>
    public string Token { get; private init; } = null!;

    /// <summary>
    /// Provider-assigned identifier for the accepted message. Non-null only when <see cref="Status"/> is
    /// <see cref="PushNotificationResponseStatus.Success"/>.
    /// </summary>
    public string? MessageId { get; private init; }

    /// <summary>
    /// Human-readable description of why delivery failed. Non-null only when <see cref="Status"/> is
    /// <see cref="PushNotificationResponseStatus.Failure"/>.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>The category of the outcome.</summary>
    public PushNotificationResponseStatus Status { get; private init; }

    /// <summary>
    /// Returns <see langword="true"/> when the notification was accepted by the provider, in which case
    /// <see cref="MessageId"/> is non-null.
    /// </summary>
    [MemberNotNullWhen(true, nameof(MessageId))]
    public bool IsSucceeded()
    {
        return Status is PushNotificationResponseStatus.Success;
    }

    /// <summary>
    /// Returns <see langword="true"/> only for an explicit delivery failure, in which case
    /// <see cref="FailureError"/> is non-null. Returns <see langword="false"/> for an unregistered token —
    /// inspect <see cref="Status"/> to distinguish that case.
    /// </summary>
    [MemberNotNullWhen(true, nameof(FailureError))]
    public bool IsFailed()
    {
        return Status is PushNotificationResponseStatus.Failure;
    }

    /// <summary>Creates a response indicating the notification was accepted for delivery.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> or <paramref name="messageId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="token"/> or <paramref name="messageId"/> is empty or white space.</exception>
    public static PushNotificationResponse Succeeded(string token, string messageId)
    {
        return new PushNotificationResponse
        {
            Status = PushNotificationResponseStatus.Success,
            Token = Argument.IsNotNullOrWhiteSpace(token),
            MessageId = Argument.IsNotNullOrWhiteSpace(messageId),
        };
    }

    /// <summary>Creates a response indicating the provider rejected the notification.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> or <paramref name="failureError"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="token"/> or <paramref name="failureError"/> is empty or white space.</exception>
    public static PushNotificationResponse Failed(string token, string failureError)
    {
        return new PushNotificationResponse
        {
            Status = PushNotificationResponseStatus.Failure,
            Token = Argument.IsNotNullOrWhiteSpace(token),
            FailureError = Argument.IsNotNullOrWhiteSpace(failureError),
            MessageId = null,
        };
    }

    /// <summary>
    /// Creates a response indicating the token is no longer registered with the provider. Callers should
    /// delete such tokens from their store.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty or white space.</exception>
    public static PushNotificationResponse Unregistered(string token)
    {
        return new PushNotificationResponse
        {
            Status = PushNotificationResponseStatus.Unregistered,
            Token = Argument.IsNotNullOrWhiteSpace(token),
            MessageId = null,
        };
    }
}

/// <summary>The category of a <see cref="PushNotificationResponse"/> outcome.</summary>
public enum PushNotificationResponseStatus
{
    /// <summary>
    /// The device token is no longer valid (for example the app was uninstalled or the token expired).
    /// The caller should remove it from their store. This is neither a success nor a failure.
    /// </summary>
    Unregistered,

    /// <summary>The notification was accepted by the provider for delivery.</summary>
    Success,

    /// <summary>The provider rejected the notification for a reason other than an unregistered token.</summary>
    Failure,
}
