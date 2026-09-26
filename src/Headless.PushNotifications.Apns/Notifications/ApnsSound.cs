// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications.Apns;

/// <summary>
/// The sound an alert notification plays, sent as the <c>sound</c> key: a named sound, or a critical sound with a
/// volume.
/// </summary>
/// <remarks>
/// The properties have no setters, so a <c>with</c> expression cannot bypass the volume check in
/// <see cref="Critical"/>.
/// </remarks>
[PublicAPI]
public sealed record ApnsSound
{
    private ApnsSound(string name, bool isCritical, double? volume)
    {
        Name = name;
        IsCritical = isCritical;
        Volume = volume;
    }

    /// <summary>The system's default notification sound.</summary>
    public static ApnsSound Default { get; } = new("default", isCritical: false, volume: null);

    /// <summary>The name of a sound file in the app bundle or its <c>Library/Sounds</c> folder, or <c>"default"</c>.</summary>
    public string Name { get; }

    /// <summary>Whether this is a critical sound, which plays even when the device is muted.</summary>
    public bool IsCritical { get; }

    /// <summary>The critical sound's volume, from 0 (silent) to 1 (full); <see langword="null"/> for a named sound.</summary>
    public double? Volume { get; }

    /// <summary>Creates a named sound.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    public static ApnsSound Named(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        return new ApnsSound(name, isCritical: false, volume: null);
    }

    /// <summary>
    /// Creates a critical sound. The app needs Apple's critical-alerts entitlement, or the device plays it as an
    /// ordinary sound.
    /// </summary>
    /// <param name="name">The sound file name, or <c>"default"</c>.</param>
    /// <param name="volume">The volume, from 0 (silent) to 1 (full).</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is blank, or <paramref name="volume"/> is outside 0 to 1.
    /// </exception>
    public static ApnsSound Critical(string name, double volume = 1)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        // NaN fails a plain range comparison in both directions, so reject it explicitly.
        if (double.IsNaN(volume))
        {
            throw new ArgumentOutOfRangeException(
                nameof(volume),
                volume,
                "The critical sound volume must be from 0 to 1."
            );
        }

        Argument.IsInclusiveBetween(volume, 0d, 1d);

        return new ApnsSound(name, isCritical: true, volume);
    }
}
