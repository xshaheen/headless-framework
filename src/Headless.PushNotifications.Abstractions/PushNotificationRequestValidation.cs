// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.PushNotifications;

/// <summary>
/// Provides the provider-neutral shape rules of a <see cref="PushNotificationRequest"/>, shared so every
/// provider accepts and rejects exactly the same requests before applying its own limits.
/// </summary>
internal static class PushNotificationRequestValidation
{
    /// <summary>
    /// Gets the maximum allowed <see cref="PushNotificationRequest.TimeToLive"/> across providers: 28 days,
    /// Firebase's maximum Android time-to-live. The cap also keeps the providers' <c>now + time-to-live</c>
    /// expiry arithmetic from overflowing.
    /// </summary>
    internal static readonly TimeSpan MaxTimeToLive = TimeSpan.FromDays(28);

    /// <summary>
    /// Validates the structure and property constraints of a push notification request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The request matches neither notification nor data-only structure, or a field is out of range.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The badge is negative, or the time-to-live is negative or exceeds 28 days.
    /// </exception>
    public static void Validate(PushNotificationRequest request)
    {
        Argument.IsNotNull(request);
        Argument.IsPositiveOrZero(request.Badge);
        Argument.IsPositiveOrZero(request.TimeToLive);

        if (request.TimeToLive is { } timeToLive)
        {
            Argument.IsLessThanOrEqualTo(
                timeToLive,
                MaxTimeToLive,
                "A push notification time-to-live cannot exceed 28 days.",
                "request.TimeToLive"
            );
        }

        if (request.Priority is { } priority)
        {
            Argument.IsInEnum(priority);
        }

        if (request.Sound is not null)
        {
            Argument.IsNotNullOrWhiteSpace(request.Sound);
        }

        if (!IsDataOnly(request))
        {
            // Setting either one makes the request a notification, which the user sees, so both must carry text.
            Argument.IsNotNullOrWhiteSpace(request.Title);
            Argument.IsNotNullOrWhiteSpace(request.Body);

            return;
        }

        if (request.Data is not { Count: > 0 })
        {
            throw new ArgumentException(
                "A push notification needs a title and a body, or data for a data-only message.",
                nameof(request)
            );
        }

        // A data-only message shows nothing to the user, so a badge or sound would make it a visible notification.
        if (request.Badge is not null || request.Sound is not null)
        {
            throw new ArgumentException(
                "A data-only push notification cannot set a badge or a sound.",
                nameof(request)
            );
        }
    }

    /// <summary>Determines whether <paramref name="request"/> represents a data-only push notification.</summary>
    /// <param name="request">The request to evaluate.</param>
    /// <returns><see langword="true"/> when the request has no title and no body; otherwise, <see langword="false"/>.</returns>
    public static bool IsDataOnly(PushNotificationRequest request)
    {
        return request.Title is null && request.Body is null;
    }
}
