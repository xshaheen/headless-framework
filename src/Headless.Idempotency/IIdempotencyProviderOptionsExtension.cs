// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Idempotency;

/// <summary>Setup-time hook through which an idempotency provider package registers its services.</summary>
/// <remarks>
/// A provider's <c>Use…</c> member registers one instance with
/// <see cref="HeadlessIdempotencySetupBuilder.RegisterExtension" />; <c>AddHeadlessIdempotency</c> calls
/// <see cref="AddServices" /> once, after checking that exactly one provider was chosen.
/// </remarks>
[PublicAPI]
public interface IIdempotencyProviderOptionsExtension
{
    /// <summary>Registers the provider's record store, options, storage initializer, and unit-of-work support.</summary>
    /// <param name="services">The application service collection.</param>
    void AddServices(IServiceCollection services);
}
