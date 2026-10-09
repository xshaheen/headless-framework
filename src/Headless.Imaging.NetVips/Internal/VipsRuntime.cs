// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using NetVips;

namespace Headless.Imaging.Internal;

/// <summary>Fails fast, with the fix in the message, when the host cannot run libvips.</summary>
/// <remarks>
/// This package references only the managed NetVips wrapper, so a host without <c>NetVips.Native</c> or a system
/// libvips would otherwise fail on the first image with a bare <see cref="TypeInitializationException" />.
/// </remarks>
internal static class VipsRuntime
{
    // 8.15 introduced the per-saver "keep" metadata flags the encoders rely on.
    private const int _MinimumMajor = 8;
    private const int _MinimumMinor = 15;

    public static void EnsureAvailable()
    {
        var initialized = ModuleInitializer.VipsInitialized;

        EnsureAvailable(
            initialized,
            ModuleInitializer.Exception,
            initialized ? global::NetVips.NetVips.Version(0) : 0,
            initialized ? global::NetVips.NetVips.Version(1) : 0
        );
    }

    /// <summary>Throws when libvips did not load or is too old; split out so each verdict can be tested.</summary>
    /// <exception cref="InvalidOperationException">libvips did not load, or it is older than 8.15.</exception>
    internal static void EnsureAvailable(bool initialized, Exception? error, int major, int minor)
    {
        if (!initialized)
        {
            throw new InvalidOperationException(
                "Headless.Imaging.NetVips could not load libvips. Add the NetVips.Native package to the application "
                    + "for the bundled binaries, or install libvips "
                    + $"{_MinimumMajor}.{_MinimumMinor} or later on the host (for example `apt-get install libvips42t64` on "
                    + "Ubuntu 24.04 or Debian 13).",
                error
            );
        }

        if (major < _MinimumMajor || (major == _MinimumMajor && minor < _MinimumMinor))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Headless.Imaging.NetVips requires libvips {_MinimumMajor}.{_MinimumMinor} or later, but the host "
                        + $"loaded libvips {major}.{minor}. Upgrade the system libvips or add the NetVips.Native package."
                )
            );
        }
    }
}
