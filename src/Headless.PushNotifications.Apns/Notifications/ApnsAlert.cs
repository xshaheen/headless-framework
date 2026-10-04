// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Represents the user-facing alert dictionary of an APNs notification.
/// </summary>
[PublicAPI]
public sealed record ApnsAlert
{
    /// <summary>Gets the notification title.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the notification subtitle.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Gets the notification body text.</summary>
    public string? Body { get; init; }

    /// <summary>Gets the localization key for the title.</summary>
    public string? TitleLocKey { get; init; }

    /// <summary>Gets format argument values for the localized title string.</summary>
    public IReadOnlyList<string>? TitleLocArgs { get; init; }

    /// <summary>
    /// Gets the localization key for the subtitle.
    /// </summary>
    public string? SubtitleLocKey { get; init; }

    /// <summary>Gets format argument values for the localized subtitle string.</summary>
    public IReadOnlyList<string>? SubtitleLocArgs { get; init; }

    /// <summary>Gets the localization key for the body text.</summary>
    public string? LocKey { get; init; }

    /// <summary>Gets format argument values for the localized body text.</summary>
    public IReadOnlyList<string>? LocArgs { get; init; }

    /// <summary>
    /// Gets the launch image filename displayed when launching from the notification.
    /// </summary>
    public string? LaunchImage { get; init; }
}
