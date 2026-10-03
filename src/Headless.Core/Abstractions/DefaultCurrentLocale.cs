// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Immutable locale that always returns <c>en</c> / <c>en-US</c> regardless of the ambient thread
/// culture. Deterministic and thread-safe — safe for background jobs, singleton scope, and tests.
/// Falls back to <see cref="CultureInfo.InvariantCulture"/> under globalization-invariant mode
/// (e.g. trimmed/container images), where <c>en-US</c> cannot be resolved.
/// </summary>
public sealed class DefaultCurrentLocale : ICurrentLocale
{
    private static readonly CultureInfo _Culture = _ResolveCulture();

    /// <inheritdoc/>
    public string Language => "en";

    /// <inheritdoc/>
    public string Locale => "en-US";

    /// <inheritdoc/>
    public CultureInfo LocaleCulture => _Culture;

    private static CultureInfo _ResolveCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
        catch (CultureNotFoundException)
        {
            // Globalization-invariant mode: only the invariant culture is available.
            return CultureInfo.InvariantCulture;
        }
    }
}
