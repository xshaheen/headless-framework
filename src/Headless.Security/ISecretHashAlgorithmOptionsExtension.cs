// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Security;

/// <summary>Setup-time hook through which an algorithm package becomes the one that writes new secret hashes.</summary>
/// <remarks>
/// An algorithm package exposes a <c>Use{Algorithm}</c> extension on <see cref="HeadlessSecretHasherSetupBuilder" />
/// that registers an implementation of this interface through
/// <see cref="HeadlessSecretHasherSetupBuilder.RegisterExtension" />. <c>AddHeadlessSecretHasher</c> requires exactly
/// one, then calls <see cref="AddServices" />, which must register an <see cref="ISecretHashAlgorithm" /> whose
/// <see cref="ISecretHashAlgorithm.Id" /> equals <see cref="AlgorithmId" />, plus that algorithm's options.
/// </remarks>
[PublicAPI]
public interface ISecretHashAlgorithmOptionsExtension
{
    /// <summary>Gets the PHC identifier of the algorithm this extension selects for new hashes.</summary>
    string AlgorithmId { get; }

    /// <summary>Registers the algorithm and its options.</summary>
    /// <param name="services">The application's service collection.</param>
    void AddServices(IServiceCollection services);
}
