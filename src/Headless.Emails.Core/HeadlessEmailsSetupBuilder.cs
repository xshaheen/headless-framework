// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>
/// Fluent builder passed to the <c>AddHeadlessEmails</c> delegate. Used to select exactly one email
/// provider via a <c>Use*</c> extension (for example <c>UseAzure</c>, <c>UseAwsSes</c>,
/// <c>UseMailkit</c>, <c>UseDevelopment</c>, <c>UseNoop</c>).
/// </summary>
/// <remarks>
/// Emails has no shared, cross-provider feature options, so the builder is provider-selection-only and
/// carries no <c>Configure</c> overloads. Each provider binds its own options inside its <c>Use*</c> member.
/// </remarks>
[PublicAPI]
public sealed class HeadlessEmailsSetupBuilder
{
    internal HeadlessEmailsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<IEmailProviderOptionsExtension> Extensions { get; } = new List<IEmailProviderOptionsExtension>();

    /// <summary>
    /// Registers a provider extension. Called internally by each <c>Use*</c> extension method; not
    /// intended for direct use by application code.
    /// </summary>
    /// <param name="extension">The provider extension to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="extension"/> is <see langword="null"/>.</exception>
    public void RegisterExtension(IEmailProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
