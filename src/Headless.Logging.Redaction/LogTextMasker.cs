// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Logging.Internal;

namespace Headless.Logging;

/// <summary>
/// Applies every registered <see cref="ILogTextRedactor"/> to text and exceptions that reach telemetry outside the
/// logger factory, so a span, a Serilog-native event, or an error-tracker hook masks the same shapes the logs do.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddHeadlessRedaction</c> registers it as a singleton. The framework uses it for exceptions recorded on spans, for
/// span status descriptions and exception tags, and for events written through Serilog's own <c>ILogger</c>. Resolve
/// it to mask a channel the framework does not write itself, such as an error tracker's before-send hook.
/// </para>
/// <para>
/// Every redactor applies, whatever <see cref="LogTextRedactionScope"/> it was registered with: a scope limits which
/// log entries a pattern scans, and these channels have no log category or level, so a scoped pattern is not allowed
/// to let a value through them.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class LogTextMasker
{
    private readonly ILogTextRedactor[] _redactors;

    internal LogTextMasker(ILogTextRedactor[] redactors)
    {
        _redactors = Argument.IsNotNull(redactors);
    }

    /// <summary>Whether any text redactor is registered; when not, every call returns its argument.</summary>
    public bool IsEnabled => _redactors.Length > 0;

    /// <summary>Returns <paramref name="text"/> with every sensitive shape masked, or the same instance when none matched.</summary>
    /// <param name="text">The text to mask.</param>
    /// <returns>The masked text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public string Mask(string text)
    {
        Argument.IsNotNull(text);

        foreach (var redactor in _redactors)
        {
            text = redactor.Redact(text);
        }

        return text;
    }

    /// <summary>
    /// Returns <paramref name="value"/> itself when its text has no match; otherwise its masked text, the way log
    /// property values are masked.
    /// </summary>
    /// <remarks>
    /// A non-string value is scanned in its invariant text form, so a 14-digit id held as a <see cref="long"/> is found.
    /// Booleans, GUIDs, enums, and date and time values are returned without being formatted.
    /// </remarks>
    /// <param name="value">The value to mask.</param>
    /// <returns>The value, or its masked text.</returns>
    public object? MaskValue(object? value)
    {
        return _redactors.Length == 0
            ? value
            : ValueMasker.Mask(value, static (text, masker) => masker.Mask(text), this);
    }

    /// <summary>
    /// Returns <paramref name="exception"/> itself when no message in its chain matched; otherwise a copy of the same
    /// type whose own message and inner exceptions are masked, keeping its stack frames, or a
    /// <see cref="RedactedException"/> when such a copy cannot be masked. The exception passed in is never changed.
    /// </summary>
    /// <param name="exception">The exception to mask.</param>
    /// <returns>The exception to hand to telemetry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public Exception Mask(Exception exception)
    {
        Argument.IsNotNull(exception);

        return _redactors.Length == 0
            ? exception
            : ExceptionMasker.Mask(exception, static (text, masker) => masker.Mask(text), this);
    }
}
