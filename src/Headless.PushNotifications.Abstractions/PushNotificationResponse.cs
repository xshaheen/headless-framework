// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.PushNotifications;

/// <summary>
/// Describes the delivery outcome for a single provider-issued client identifier.
/// </summary>
/// <remarks>
/// Exactly one status applies. A successful send carries a message identifier. A failed send carries
/// an error description. An unregistered identifier carries neither and indicates that the identifier
/// must be removed from storage.
/// </remarks>
[PublicAPI]
public sealed record PushNotificationResponse
{
    private PushNotificationResponse() { }

    /// <summary>Gets the provider-issued client identifier for this response.</summary>
    public string ClientIdentifier { get; private init; } = null!;

    /// <summary>
    /// Gets the provider-assigned identifier for an accepted message.
    /// This property is populated when <see cref="Status"/> is <see cref="PushNotificationResponseStatus.Success"/>.
    /// </summary>
    public string? MessageId { get; private init; }

    /// <summary>
    /// Gets the description of why delivery failed.
    /// This property is populated when <see cref="Status"/> is <see cref="PushNotificationResponseStatus.Failure"/>.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>Gets the delivery outcome status.</summary>
    public PushNotificationResponseStatus Status { get; private init; }

    /// <summary>
    /// Returns <see langword="true"/> when the provider accepted the notification.
    /// </summary>
    /// <returns><see langword="true"/> when delivery succeeded; otherwise, <see langword="false"/>.</returns>
    [MemberNotNullWhen(true, nameof(MessageId))]
    public bool IsSucceeded()
    {
        return Status is PushNotificationResponseStatus.Success;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the provider rejected delivery with an error.
    /// </summary>
    /// <returns><see langword="true"/> when delivery failed; otherwise, <see langword="false"/>.</returns>
    [MemberNotNullWhen(true, nameof(FailureError))]
    public bool IsFailed()
    {
        return Status is PushNotificationResponseStatus.Failure;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the client identifier is no longer registered.
    /// </summary>
    /// <returns><see langword="true"/> when the identifier is unregistered; otherwise, <see langword="false"/>.</returns>
    public bool IsUnregistered()
    {
        return Status is PushNotificationResponseStatus.Unregistered;
    }

    /// <summary>Creates a response indicating delivery acceptance.</summary>
    /// <param name="clientIdentifier">The provider-issued client identifier.</param>
    /// <param name="messageId">The provider-assigned message identifier.</param>
    /// <returns>A successful response instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientIdentifier"/> or <paramref name="messageId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="clientIdentifier"/> or <paramref name="messageId"/> is empty or white space.</exception>
    public static PushNotificationResponse Succeeded(string clientIdentifier, string messageId)
    {
        return new PushNotificationResponse
        {
            Status = PushNotificationResponseStatus.Success,
            ClientIdentifier = Argument.IsNotNullOrWhiteSpace(clientIdentifier),
            MessageId = Argument.IsNotNullOrWhiteSpace(messageId),
        };
    }

    /// <summary>
    /// Creates a successful response without validating arguments.
    /// </summary>
    /// <param name="clientIdentifier">The client identifier.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>A successful response instance.</returns>
    internal static PushNotificationResponse SucceededUnchecked(string clientIdentifier, string messageId)
    {
        return new PushNotificationResponse
        {
            Status = PushNotificationResponseStatus.Success,
            ClientIdentifier = clientIdentifier,
            MessageId = messageId,
        };
    }

    /// <summary>Creates a response indicating delivery failure.</summary>
    /// <param name="clientIdentifier">The provider-issued client identifier.</param>
    /// <param name="failureError">The failure description.</param>
    /// <returns>A failed response instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientIdentifier"/> or <paramref name="failureError"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="clientIdentifier"/> or <paramref name="failureError"/> is empty or white space.</exception>
    public static PushNotificationResponse Failed(string clientIdentifier, string failureError)
    {
        return new PushNotificationResponse
        {
            Status = PushNotificationResponseStatus.Failure,
            ClientIdentifier = Argument.IsNotNullOrWhiteSpace(clientIdentifier),
            FailureError = Argument.IsNotNullOrWhiteSpace(failureError),
            MessageId = null,
        };
    }

    /// <summary>
    /// Creates a response indicating that the client identifier is no longer registered.
    /// </summary>
    /// <param name="clientIdentifier">The unregistered client identifier.</param>
    /// <returns>An unregistered response instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientIdentifier"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="clientIdentifier"/> is empty or white space.</exception>
    public static PushNotificationResponse Unregistered(string clientIdentifier)
    {
        return new PushNotificationResponse
        {
            Status = PushNotificationResponseStatus.Unregistered,
            ClientIdentifier = Argument.IsNotNullOrWhiteSpace(clientIdentifier),
            MessageId = null,
        };
    }
}

/// <summary>Specifies the delivery outcome status of a push notification.</summary>
/// <remarks>
/// Prefer <see cref="PushNotificationResponse.IsSucceeded"/>, <see cref="PushNotificationResponse.IsFailed"/>,
/// and <see cref="PushNotificationResponse.IsUnregistered"/> when checking outcomes.
/// </remarks>
[PublicAPI]
public enum PushNotificationResponseStatus
{
    /// <summary>
    /// The client identifier is no longer valid. The caller must remove it from storage.
    /// </summary>
    Unregistered = 0,

    /// <summary>The provider accepted the notification for delivery.</summary>
    Success = 1,

    /// <summary>The provider rejected the notification with an error.</summary>
    Failure = 2,
}
