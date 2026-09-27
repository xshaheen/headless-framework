// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sequences;

/// <summary>Setup-time hook through which a sequence provider package registers its services.</summary>
/// <remarks>
/// A provider's <c>Use…</c> member registers one instance with
/// <see cref="HeadlessSequencesSetupBuilder.RegisterExtension" />; <c>AddHeadlessSequences</c> calls
/// <see cref="AddServices" /> once, after checking that exactly one provider was chosen.
/// </remarks>
[PublicAPI]
public interface ISequencesProviderOptionsExtension
{
    /// <summary>Registers the provider's store, options, and storage initializer.</summary>
    /// <param name="services">The application service collection.</param>
    void AddServices(IServiceCollection services);
}
