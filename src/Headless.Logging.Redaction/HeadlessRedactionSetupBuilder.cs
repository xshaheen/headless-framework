// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Checks;
using Headless.Logging.Internal;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Logging;

/// <summary>Configures redaction inside <c>AddHeadlessRedaction(setup =&gt; …)</c>.</summary>
/// <remarks>
/// Each call to <c>AddHeadlessRedaction</c> applies only what its own builder asked for, so an app and the libraries it
/// uses can each call it in any order: a library's bare call never undoes the app's <see cref="UseHmac(IConfigurationSection)"/>
/// or its text patterns.
/// </remarks>
[PublicAPI]
public sealed class HeadlessRedactionSetupBuilder
{
    // Microsoft's MatchTimeout default is infinite, and a text pattern runs on every log entry.
    private static readonly TimeSpan _DefaultMatchTimeout = TimeSpan.FromMilliseconds(100);

    internal HeadlessRedactionSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal List<Action<IRedactionBuilder>> RedactionActions { get; } = [];

    /// <summary>
    /// Replaces <see cref="PrivateDataAttribute"/> and <see cref="IdentifierDataAttribute"/> values with an HMAC
    /// pseudonym instead of erasing them, keyed from <paramref name="hmacSection"/>.
    /// </summary>
    /// <remarks>
    /// The same value always yields the same pseudonym under one key, so operators can tell that two events concern the
    /// same person or id; the pseudonym cannot be reversed without the key. Rotating the key breaks that correlation
    /// across the rotation. The section binds <see cref="HmacRedactorOptions"/>: a Base64 <c>Key</c> of at least 44
    /// characters and a <c>KeyId</c> appended to each pseudonym. Keep the key in a secret store.
    /// <see cref="SecretDataAttribute"/> values are erased even with a key.
    /// </remarks>
    /// <param name="hmacSection">The configuration section that binds <see cref="HmacRedactorOptions"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hmacSection"/> is <see langword="null"/>.</exception>
    public HeadlessRedactionSetupBuilder UseHmac(IConfigurationSection hmacSection)
    {
        Argument.IsNotNull(hmacSection);

        RedactionActions.Add(redaction => redaction.SetHmacRedactor(hmacSection, _Pseudonymized));

        return this;
    }

    /// <summary>
    /// Replaces <see cref="PrivateDataAttribute"/> and <see cref="IdentifierDataAttribute"/> values with an HMAC
    /// pseudonym instead of erasing them, keyed by <paramref name="configureHmac"/>.
    /// </summary>
    /// <remarks>
    /// The same value always yields the same pseudonym under one key; rotating the key breaks that correlation across
    /// the rotation. <see cref="HmacRedactorOptions.Key"/> is a Base64 key of at least 44 characters; keep it in a
    /// secret store. <see cref="SecretDataAttribute"/> values are erased even with a key.
    /// </remarks>
    /// <param name="configureHmac">Sets the HMAC key and key id.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configureHmac"/> is <see langword="null"/>.</exception>
    public HeadlessRedactionSetupBuilder UseHmac(Action<HmacRedactorOptions> configureHmac)
    {
        Argument.IsNotNull(configureHmac);

        RedactionActions.Add(redaction => redaction.SetHmacRedactor(configureHmac, _Pseudonymized));

        return this;
    }

    /// <summary>
    /// Changes Microsoft's redaction directly: map your own classifications to redactors, or replace a Headless
    /// default.
    /// </summary>
    /// <param name="configure">Runs after the Headless defaults.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessRedactionSetupBuilder ConfigureRedaction(Action<IRedactionBuilder> configure)
    {
        Argument.IsNotNull(configure);

        RedactionActions.Add(configure);

        return this;
    }

    /// <summary>
    /// Masks every match of <paramref name="pattern"/> in the text of log entries: the rendered message, property
    /// values, and scope values. For value shapes no classification reaches.
    /// </summary>
    /// <remarks>
    /// The pattern is compiled culture-invariant with a 100 ms match timeout. A timed-out match replaces the whole
    /// text, so a slow match never lets the value through. Exception messages are not rewritten.
    /// </remarks>
    /// <param name="pattern">The shape to mask, for example <c>@"\b\d{14}\b"</c>.</param>
    /// <param name="replacement">The text that replaces each match.</param>
    /// <param name="scope">Limits the categories and levels the pattern scans; every entry when omitted.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> is <see langword="null"/> or empty.</exception>
    public HeadlessRedactionSetupBuilder MaskText(
        string pattern,
        string replacement = "***",
        Action<LogTextRedactionScope>? scope = null
    )
    {
        Argument.IsNotNullOrEmpty(pattern);

        var regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, _DefaultMatchTimeout);

        return MaskText(regex, replacement, scope);
    }

    /// <summary>
    /// Masks every match of <paramref name="pattern"/> in the text of log entries: the rendered message, property
    /// values, and scope values. For value shapes no classification reaches.
    /// </summary>
    /// <remarks>
    /// Give the pattern a match timeout, because it runs on every scanned entry: a timed-out match replaces the whole
    /// text. Exception messages are not rewritten.
    /// </remarks>
    /// <param name="pattern">The shape to mask.</param>
    /// <param name="replacement">The text that replaces each match.</param>
    /// <param name="scope">Limits the categories and levels the pattern scans; every entry when omitted.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    public HeadlessRedactionSetupBuilder MaskText(
        Regex pattern,
        string replacement = "***",
        Action<LogTextRedactionScope>? scope = null
    )
    {
        Argument.IsNotNull(pattern);
        Argument.IsNotNull(replacement);

        return MaskText(new RegexLogTextRedactor(pattern, replacement), scope);
    }

    /// <summary>Masks log text with a redactor of your own.</summary>
    /// <param name="redactor">The redactor.</param>
    /// <param name="scope">Limits the categories and levels the redactor scans; every entry when omitted.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="redactor"/> is <see langword="null"/>.</exception>
    public HeadlessRedactionSetupBuilder MaskText(
        ILogTextRedactor redactor,
        Action<LogTextRedactionScope>? scope = null
    )
    {
        Argument.IsNotNull(redactor);

        var options = new LogTextRedactionScope();
        scope?.Invoke(options);

        Services.AddSingleton(new LogTextRedactorRegistration(redactor, options.MinimumLevel, [.. options.Categories]));

        return this;
    }

    // Private data and identifiers are what operators correlate on, so they are the classifications an HMAC key
    // pseudonymizes; a secret is never correlated on, so it is always erased.
    private static readonly DataClassificationSet[] _Pseudonymized =
    [
        new(HeadlessDataClassifications.Private),
        new(HeadlessDataClassifications.Identifier),
    ];
}
