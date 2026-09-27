// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// The provider-neutral shape rules of a <see cref="PushNotificationRequest"/>, shared so every provider accepts and
/// rejects exactly the same requests before applying its own limits.
/// </summary>
internal static class PushNotificationRequestValidation
{
    /// <summary>
    /// The longest <see cref="PushNotificationRequest.TimeToLive"/> any provider accepts: 28 days, Firebase's maximum
    /// Android time-to-live. The cap also keeps the providers' <c>now + time-to-live</c> expiry arithmetic from
    /// overflowing.
    /// </summary>
    internal static readonly TimeSpan MaxTimeToLive = TimeSpan.FromDays(28);

    /// <summary>
    /// Throws unless <paramref name="request"/> is either a notification (non-blank title and body) or a data-only
    /// message (no title or body, at least one data entry, no badge or sound), with a non-negative badge, a
    /// time-to-live between zero and 28 days, and a defined priority.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The request matches neither kind, or a field is out of range.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The time-to-live is negative or longer than 28 days.</exception>
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

    /// <summary>Whether <paramref name="request"/> has neither a title nor a body.</summary>
    public static bool IsDataOnly(PushNotificationRequest request)
    {
        return request.Title is null && request.Body is null;
    }
}
