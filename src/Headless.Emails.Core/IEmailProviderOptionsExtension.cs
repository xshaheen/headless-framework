// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails;

/// <summary>Setup-time extension hook implemented by each email provider package.</summary>
/// <remarks>
/// Provider packages create an internal implementation of this interface and register it via
/// <see cref="HeadlessEmailsSetupBuilder.RegisterExtension"/>. Exactly one extension must be
/// registered per <c>AddHeadlessEmails</c> call; the core setup validates this constraint and
/// throws <see cref="InvalidOperationException"/> if zero or multiple providers are found.
/// </remarks>
[PublicAPI]
public interface IEmailProviderOptionsExtension
{
    /// <summary>Registers the provider-specific <see cref="IEmailSender"/> and its dependencies into <paramref name="services"/>.</summary>
    /// <param name="services">The application's <see cref="IServiceCollection"/>.</param>
    void AddServices(IServiceCollection services);
}
