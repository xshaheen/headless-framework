// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Headless.Checks;

namespace Headless.Reliability;

/// <summary>
/// Numeric values that replace the matching fields of an already resolved <see cref="FailurePolicyDefinition"/>,
/// typically bound from configuration so an operator can tune retry counts and delays without a code change.
/// </summary>
/// <remarks>
/// A <see langword="null"/> property keeps the resolved value. Fail rules are code, so they cannot be overridden here
/// and always come from the resolved policy. Apply the overrides with <see cref="FailurePolicyDefinition.With"/>,
/// which validates the combined result the same way the builder does. The properties are settable so configuration
/// binding can populate them; the definition copies the values, so later changes to this object do not affect it.
/// </remarks>
[PublicAPI]
public sealed class FailurePolicyOverrides
{
    /// <summary>Replaces <see cref="FailurePolicyDefinition.ImmediateRetries"/> when set.</summary>
    public int? ImmediateRetries { get; set; }

    /// <summary>Replaces <see cref="FailurePolicyDefinition.DelayedRetries"/> when set.</summary>
    public int? DelayedRetries { get; set; }

    /// <summary>Replaces <see cref="FailurePolicyDefinition.DelayedInitialDelay"/> when set.</summary>
    public TimeSpan? DelayedInitialDelay { get; set; }

    /// <summary>Replaces <see cref="FailurePolicyDefinition.DelayedMaxDelay"/> when set.</summary>
    public TimeSpan? DelayedMaxDelay { get; set; }

    /// <summary>
    /// Reads overrides from the children of a <c>FailurePolicy</c> configuration section, given as plain key/value
    /// settings so this package does not depend on a configuration library.
    /// </summary>
    /// <param name="settings">
    /// Each child setting: its <c>Key</c> (matched case-insensitively against <c>ImmediateRetries</c>,
    /// <c>DelayedRetries</c>, <c>DelayedInitialDelay</c>, and <c>DelayedMaxDelay</c>), its full configuration
    /// <c>Path</c> used to name it in an error, and its raw <c>Value</c>.
    /// </param>
    /// <param name="errors">Receives one message per unknown key or unparseable value.</param>
    /// <param name="overrides">The parsed overrides when every setting was valid; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when every setting was valid; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> or <paramref name="errors"/> is null.</exception>
    /// <remarks>
    /// Retry counts parse as invariant-culture integers and delays as invariant-culture <see cref="TimeSpan"/> values.
    /// Range checks are not applied here: <see cref="FailurePolicyDefinition.With"/> validates the combined result.
    /// Every setting is read even after a failure, so one call reports every problem at once.
    /// </remarks>
    public static bool TryParse(
        IEnumerable<(string Key, string Path, string? Value)> settings,
        ICollection<string> errors,
        [NotNullWhen(true)] out FailurePolicyOverrides? overrides
    )
    {
        Argument.IsNotNull(settings);
        Argument.IsNotNull(errors);

        var parsed = new FailurePolicyOverrides();
        var valid = true;

        foreach (var (key, path, value) in settings)
        {
            if (string.Equals(key, nameof(ImmediateRetries), StringComparison.OrdinalIgnoreCase))
            {
                parsed.ImmediateRetries = _ReadRetries(path, value, errors, ref valid);
            }
            else if (string.Equals(key, nameof(DelayedRetries), StringComparison.OrdinalIgnoreCase))
            {
                parsed.DelayedRetries = _ReadRetries(path, value, errors, ref valid);
            }
            else if (string.Equals(key, nameof(DelayedInitialDelay), StringComparison.OrdinalIgnoreCase))
            {
                parsed.DelayedInitialDelay = _ReadDelay(path, value, errors, ref valid);
            }
            else if (string.Equals(key, nameof(DelayedMaxDelay), StringComparison.OrdinalIgnoreCase))
            {
                parsed.DelayedMaxDelay = _ReadDelay(path, value, errors, ref valid);
            }
            else
            {
                errors.Add(
                    $"Configuration '{path}' is not a failure policy setting. The supported settings are "
                        + "ImmediateRetries, DelayedRetries, DelayedInitialDelay, and DelayedMaxDelay."
                );
                valid = false;
            }
        }

        overrides = valid ? parsed : null;

        return valid;
    }

    private static int? _ReadRetries(string path, string? value, ICollection<string> errors, ref bool valid)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retries))
        {
            return retries;
        }

        errors.Add($"Configuration '{path}' must be an integer.");
        valid = false;

        return null;
    }

    private static TimeSpan? _ReadDelay(string path, string? value, ICollection<string> errors, ref bool valid)
    {
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var delay))
        {
            return delay;
        }

        errors.Add($"Configuration '{path}' must be a duration such as '00:00:30'.");
        valid = false;

        return null;
    }
}
