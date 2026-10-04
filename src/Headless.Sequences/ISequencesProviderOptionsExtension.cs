// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sequences;

/// <summary>Defines the registration contract implemented by sequence database provider packages.</summary>
/// <remarks>
/// A provider registration method calls <see cref="HeadlessSequencesSetupBuilder.RegisterExtension" /> to register its extension.
/// <c>AddHeadlessSequences</c> invokes <see cref="AddServices" /> during setup.
/// </remarks>
[PublicAPI]
public interface ISequencesProviderOptionsExtension
{
    /// <summary>Registers the provider store, options, and initialization services.</summary>
    /// <param name="services">The application service collection.</param>
    void AddServices(IServiceCollection services);
}
