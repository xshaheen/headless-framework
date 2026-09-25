// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// The visible content of a notification, written as the <c>aps.alert</c> dictionary.
/// </summary>
/// <remarks>
/// <para>
/// Each text can be a literal or a localization key the app resolves from its <c>Localizable.strings</c>, with
/// optional format arguments. Set the literal or the key for a text, not both; arguments need their key.
/// </para>
/// <para>
/// A Live Activity alert shows only a title and a body, so its alert may not set <see cref="Subtitle"/>,
/// <see cref="SubtitleLocKey"/>, <see cref="SubtitleLocArgs"/>, or <see cref="LaunchImage"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record ApnsAlert
{
    /// <summary>The title, written as <c>title</c>.</summary>
    public string? Title { get; init; }

    /// <summary>The subtitle, written as <c>subtitle</c>.</summary>
    public string? Subtitle { get; init; }

    /// <summary>The body text, written as <c>body</c>.</summary>
    public string? Body { get; init; }

    /// <summary>The localization key for the title, written as <c>title-loc-key</c> in place of <see cref="Title"/>.</summary>
    public string? TitleLocKey { get; init; }

    /// <summary>The format arguments for <see cref="TitleLocKey"/>, written as <c>title-loc-args</c>.</summary>
    public IReadOnlyList<string>? TitleLocArgs { get; init; }

    /// <summary>
    /// The localization key for the subtitle, written as <c>subtitle-loc-key</c> in place of <see cref="Subtitle"/>.
    /// </summary>
    public string? SubtitleLocKey { get; init; }

    /// <summary>The format arguments for <see cref="SubtitleLocKey"/>, written as <c>subtitle-loc-args</c>.</summary>
    public IReadOnlyList<string>? SubtitleLocArgs { get; init; }

    /// <summary>The localization key for the body, written as <c>loc-key</c> in place of <see cref="Body"/>.</summary>
    public string? LocKey { get; init; }

    /// <summary>The format arguments for <see cref="LocKey"/>, written as <c>loc-args</c>.</summary>
    public IReadOnlyList<string>? LocArgs { get; init; }

    /// <summary>
    /// The image file the app shows while launching from the notification, written as <c>launch-image</c>.
    /// </summary>
    public string? LaunchImage { get; init; }
}
