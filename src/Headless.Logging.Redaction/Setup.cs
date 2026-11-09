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
        /// Registers Microsoft's redaction for the Headless taxonomy and applies it to every log entry, whichever logging
        /// stack owns the <see cref="ILoggerFactory"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="SecretDataAttribute"/> values are always erased. <see cref="PrivateDataAttribute"/> and
        /// <see cref="IdentifierDataAttribute"/> values, and any classification without a redactor of its own, are
        /// erased too, unless <see cref="HeadlessRedactionSetupBuilder.UseHmac(IConfigurationSection)"/> pseudonymizes them.
        /// Erasing keeps the property and its name in the log with an empty value.
        /// </para>
        /// <para>
        /// The registered logger factory is wrapped, so a classified <c>[LoggerMessage]</c> parameter is redacted under
        /// Serilog's factory too, credential paths an <see cref="ILogPathRedactor"/> recognizes are replaced in ASP.NET
        /// Core's request scope and request lines, and text patterns from
        /// <see cref="HeadlessRedactionSetupBuilder.MaskText(string, string, Action{LogTextRedactionScope})"/> are
        /// masked. A logger factory registered later replaces the wrapped one: <c>AddHeadlessSerilog</c> from
        /// <c>Headless.Logging.Serilog</c> wraps its own, and after a plain <c>UseSerilog</c> call this method again.
        /// The host logs a warning at startup when the factory in charge is not wrapped.
        /// </para>
        /// <para>
        /// Safe to call from an app and from libraries, in any order: each call adds only what its own
        /// <paramref name="setup"/> asks for, and a factory is never wrapped twice.
        /// </para>
        /// </remarks>
        /// <param name="setup">Adds an HMAC key, text patterns, or mappings of your own.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        public IServiceCollection AddHeadlessRedaction(Action<HeadlessRedactionSetupBuilder>? setup = null)
        {
            Argument.IsNotNull(services);

            var builder = new HeadlessRedactionSetupBuilder(services);
            setup?.Invoke(builder);

            services.AddRedaction(redaction =>
            {
                // Only the secret mapping is applied on every call. Private data and identifiers stay with the fallback
                // (erasing) until a UseHmac maps them, so a library's bare call never undoes an app's key.
                redaction.SetRedactor<ErasingRedactor>(new DataClassificationSet(HeadlessDataClassifications.Secret));

                foreach (var action in builder.RedactionActions)
                {
                    action(redaction);
                }
            });

            // Every ILoggerFactory registration is wrapped, so the last one, the one the container resolves, always is.
            services.TryDecorateWith<ILoggerFactory>(
                (inner, provider) =>
                    inner is RedactingLoggerFactory
                        ? inner
                        : new RedactingLoggerFactory(
                            inner,
                            provider.GetService<IRedactorProvider>(),
                            [.. provider.GetServices<ILogPathRedactor>()],
                            LogTextRedactorRegistration.All(provider)
                        )
            );

            services.AddStartupValidator<LogRedactionStartupValidator>();

            return services;
        }
    }
}
