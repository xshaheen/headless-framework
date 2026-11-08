// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Headless.Logging.Internal;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Logging;

/// <summary>Registers the redactors for the <see cref="HeadlessDataClassifications"/> taxonomy.</summary>
[PublicAPI]
public static class SetupLoggingRedaction
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Microsoft's redaction with every Headless classification erased: <see cref="SecretDataAttribute"/>,
        /// <see cref="PrivateDataAttribute"/>, and <see cref="IdentifierDataAttribute"/> members, and any
        /// classification without a redactor of its own.
        /// </summary>
        /// <remarks>
        /// Erasing keeps the property and its name in the log with an empty value. To keep "same person" or "same id"
        /// searchable, use an overload that takes an HMAC key, which pseudonymizes private data and identifiers.
        /// </remarks>
        /// <param name="configure">Runs after the defaults, so it can map further classifications or replace a default.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        public IServiceCollection AddHeadlessRedaction(Action<IRedactionBuilder>? configure = null)
        {
            Argument.IsNotNull(services);

            return services.AddRedaction(redaction =>
            {
                redaction.SetRedactor<ErasingRedactor>(_Pseudonymized);
                _AddDefaults(redaction, configure);
            });
        }

        /// <summary>
        /// Registers Microsoft's redaction with <see cref="SecretDataAttribute"/> members erased, and
        /// <see cref="PrivateDataAttribute"/> and <see cref="IdentifierDataAttribute"/> members replaced by an HMAC
        /// pseudonym keyed from <paramref name="hmacSection"/>.
        /// </summary>
        /// <remarks>
        /// The same value always yields the same pseudonym under one key, so operators can tell that two events concern
        /// the same person or id; the pseudonym cannot be reversed without the key. Rotating the key breaks that
        /// correlation across the rotation. The section binds <see cref="HmacRedactorOptions"/>: a Base64 <c>Key</c> of
        /// at least 44 characters and a <c>KeyId</c> appended to each pseudonym. Keep the key in a secret store.
        /// </remarks>
        /// <param name="hmacSection">The configuration section that binds <see cref="HmacRedactorOptions"/>.</param>
        /// <param name="configure">Runs after the defaults, so it can map further classifications or replace a default.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        public IServiceCollection AddHeadlessRedaction(
            IConfigurationSection hmacSection,
            Action<IRedactionBuilder>? configure = null
        )
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(hmacSection);

            return services.AddRedaction(redaction =>
            {
                redaction.SetHmacRedactor(hmacSection, _Pseudonymized);
                _AddDefaults(redaction, configure);
            });
        }

        /// <summary>
        /// Registers Microsoft's redaction with <see cref="SecretDataAttribute"/> members erased, and
        /// <see cref="PrivateDataAttribute"/> and <see cref="IdentifierDataAttribute"/> members replaced by an HMAC
        /// pseudonym keyed by <paramref name="configureHmac"/>.
        /// </summary>
        /// <remarks>
        /// The same value always yields the same pseudonym under one key; rotating the key breaks that correlation
        /// across the rotation. <see cref="HmacRedactorOptions.Key"/> is a Base64 key of at least 44 characters; keep it
        /// in a secret store.
        /// </remarks>
        /// <param name="configureHmac">Sets the HMAC key and key id.</param>
        /// <param name="configure">Runs after the defaults, so it can map further classifications or replace a default.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        public IServiceCollection AddHeadlessRedaction(
            Action<HmacRedactorOptions> configureHmac,
            Action<IRedactionBuilder>? configure = null
        )
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(configureHmac);

            return services.AddRedaction(redaction =>
            {
                redaction.SetHmacRedactor(configureHmac, _Pseudonymized);
                _AddDefaults(redaction, configure);
            });
        }

        /// <summary>
        /// Wraps the registered <see cref="ILoggerFactory"/> so every log entry is redacted before any provider sees it:
        /// classified <c>[LoggerMessage]</c> parameters through the registered <see cref="IRedactorProvider"/>, and
        /// credential paths that an <see cref="ILogPathRedactor"/> recognizes in ASP.NET Core's request scope and
        /// request lines.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The wrapper works whichever logging stack owns the factory. Under Serilog's <c>UseSerilog</c> it is the only
        /// thing that redacts a classified <c>[LoggerMessage]</c> parameter: without it the parameter is dropped and the
        /// text keeps a literal <c>{Name}</c> hole, because Serilog's factory never runs Microsoft's
        /// <c>EnableRedaction</c>.
        /// </para>
        /// <para>
        /// Call it after every call that registers a logger factory, such as <c>UseSerilog</c>: a factory registered
        /// later replaces the wrapped one. Calling it again is safe, and wraps the factories registered since; a factory
        /// is never wrapped twice. When a later registration does replace it, the host logs a warning at startup.
        /// </para>
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        public IServiceCollection AddHeadlessLogRedaction()
        {
            Argument.IsNotNull(services);

            // Every ILoggerFactory registration is wrapped, so the last one, the one the container resolves, always is.
            services.TryDecorateWith<ILoggerFactory>(
                (inner, provider) =>
                    inner is RedactingLoggerFactory
                        ? inner
                        : new RedactingLoggerFactory(
                            inner,
                            provider.GetService<IRedactorProvider>(),
                            [.. provider.GetServices<ILogPathRedactor>()]
                        )
            );

            services.AddStartupValidator<LogRedactionStartupValidator>();

            return services;
        }
    }

    // Private data and identifiers are what operators correlate on, so they are the classifications an HMAC key
    // pseudonymizes; a secret is never correlated on, so it is always erased.
    private static readonly DataClassificationSet[] _Pseudonymized =
    [
        new(HeadlessDataClassifications.Private),
        new(HeadlessDataClassifications.Identifier),
    ];

    private static void _AddDefaults(IRedactionBuilder redaction, Action<IRedactionBuilder>? configure)
    {
        redaction.SetRedactor<ErasingRedactor>(new DataClassificationSet(HeadlessDataClassifications.Secret));

        // A member carrying a classification nobody mapped, or a combination of classifications, fails closed.
        redaction.SetFallbackRedactor<ErasingRedactor>();

        configure?.Invoke(redaction);
    }
}
