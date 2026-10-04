// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.PushNotifications;

/// <summary>
/// Provides validation rules for <see cref="PushNotificationRequest"/> instances.
/// </summary>
internal static class PushNotificationRequestValidation
{
    /// <summary>
    /// Gets the maximum allowed <see cref="PushNotificationRequest.TimeToLive"/> across providers.
    /// </summary>
    internal static readonly TimeSpan MaxTimeToLive = TimeSpan.FromDays(28);

    /// <summary>
    /// Validates the structure and property constraints of a push notification request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The request matches neither notification nor data-only structure, or contains invalid property values.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The time-to-live is negative or exceeds 28 days.</exception>
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
            // Both fields are required for visible notifications.
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

        // Data-only messages run in the background and cannot display visual or audible alerts.
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
