// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks;

/// <summary>
/// Setup-time extension hook implemented by each distributed-lock backend provider package.
/// Provider packages register an implementation of this interface via
/// <see cref="HeadlessDistributedLocksSetupBuilder.RegisterExtension"/> from within their
/// <c>Use*</c> builder extension methods. The framework core calls <see cref="AddServices"/>
/// once, after validating that exactly one provider has been registered.
/// </summary>
[PublicAPI]
public interface IDistributedLocksOptionsExtension
{
    /// <summary>
    /// Registers the provider's concrete storage and any required ancillary services into
    /// <paramref name="services"/>. Called once by the framework core during
    /// <see cref="SetupDistributedLocks"/> setup; implementations should be idempotent.
    /// </summary>
    /// <param name="services">The DI service collection to register services into.</param>
    void AddServices(IServiceCollection services);
}
