// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Masks sensitive text and exceptions with the app's text redactors before a feature writes them to a channel that
/// cannot be rewritten afterwards, such as an exception event recorded on a span.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddHeadlessRedaction</c> (package <c>Headless.Logging.Redaction</c>) registers the implementation. A feature
/// resolves it as optional with <c>GetService</c> and writes the raw value when no implementation is registered, so it
/// does not need a reference to the redaction package.
/// </para>
/// <para>
/// Record a span exception event with <see cref="HeadlessLogTextMaskerExtensions.RecordException"/>, which masks the
/// event's message and its rendered text, not only the exception it is given.
/// </para>
/// </remarks>
[PublicAPI]
public interface ILogTextMasker
{
    /// <summary>Returns <paramref name="text"/> with every sensitive shape masked, or the same instance when none matched.</summary>
    /// <param name="text">The text to mask.</param>
    /// <returns>The masked text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    string Mask(string text);

    /// <summary>
    /// Returns <paramref name="exception"/> itself when nothing in it matched; otherwise an exception to hand to
    /// telemetry in its place, with the matching text masked. The exception passed in is never changed.
    /// </summary>
    /// <remarks>
    /// The returned exception's <see cref="Exception.Message"/> is masked as <see cref="Mask(string)"/> would mask the
    /// original's: <see cref="HeadlessLogTextMaskerExtensions.RecordException"/> records it without scanning it again.
    /// </remarks>
    /// <param name="exception">The exception to mask.</param>
    /// <returns>The exception to hand to telemetry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    Exception Mask(Exception exception);
}
