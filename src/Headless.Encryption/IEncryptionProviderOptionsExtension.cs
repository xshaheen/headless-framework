// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Encryption;

/// <summary>
/// Defines the setup-time hook through which a key-store or key-encryption-key provider package registers its
/// services.
/// </summary>
/// <remarks>
/// A provider's <c>Use…</c> member registers one instance with
/// <see cref="HeadlessEncryptionSetupBuilder.RegisterKeyStore" /> or
/// <see cref="HeadlessEncryptionSetupBuilder.RegisterKeyEncryptionKeyProvider" />; <c>AddHeadlessEncryption</c> calls
/// <see cref="AddServices" /> once, after checking that exactly one of each was chosen.
/// </remarks>
[PublicAPI]
public interface IEncryptionProviderOptionsExtension
{
    /// <summary>Registers the provider's services and options.</summary>
    /// <param name="services">The application service collection.</param>
    void AddServices(IServiceCollection services);
}
