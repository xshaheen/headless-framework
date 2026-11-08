// Copyright (c) Mahmoud Shaheen. All rights reserved.
// Adapted from Serilog.Redaction (https://github.com/zyofeng/serilog-redaction), Apache License 2.0; modified.
// See THIRD-PARTY-NOTICES.md in this package.

using Headless.Checks;
using Microsoft.Extensions.Compliance.Redaction;
using Serilog;
using Serilog.Configuration;

namespace Headless.Logging;

[PublicAPI]
public static class DataClassificationDestructuringExtensions
{
    /// <summary>
    /// Adds a <see cref="DataClassificationDestructuringPolicy"/>, so members marked with a
    /// <c>DataClassificationAttribute</c> are redacted through <paramref name="redactorProvider"/> whenever Serilog
    /// destructures them.
    /// </summary>
    /// <remarks>
    /// Serilog tries destructuring policies in the order they are added and uses the first that handles a value, so
    /// add this one before any policy that would handle the same types. <c>ConfigureReloadableLoggerConfiguration</c>
    /// adds it first when the container has an <see cref="IRedactorProvider"/> (registered by
    /// <c>services.AddRedaction(...)</c>); call this directly only for a logger built without it.
    /// </remarks>
    /// <param name="configuration">The logger's destructuring configuration.</param>
    /// <param name="redactorProvider">The provider that maps each classification to its redactor.</param>
    /// <returns>The logger configuration, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configuration"/> or <paramref name="redactorProvider"/> is <see langword="null"/>.
    /// </exception>
    public static LoggerConfiguration WithRedaction(
        this LoggerDestructuringConfiguration configuration,
        IRedactorProvider redactorProvider
    )
    {
        Argument.IsNotNull(configuration);
        Argument.IsNotNull(redactorProvider);

        return configuration.With(new DataClassificationDestructuringPolicy(redactorProvider));
    }
}
