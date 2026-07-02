// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks;

/// <summary>
/// Fluent builder passed to the <c>configure</c> callback of
/// <see cref="SetupDistributedLocks"/> that lets callers bind global lock options and register
/// exactly one backend provider before the DI container is finalized.
/// </summary>
/// <remarks>
/// Exactly one provider extension must be registered via <see cref="RegisterExtension"/> before
/// <see cref="SetupDistributedLocks"/> completes; zero or multiple registrations throw
/// <see cref="InvalidOperationException"/> at setup time.
/// </remarks>
[PublicAPI]
public sealed class HeadlessDistributedLocksSetupBuilder
{
    internal HeadlessDistributedLocksSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<IDistributedLocksOptionsExtension> Extensions { get; } = [];

    /// <summary>
    /// Binds <see cref="DistributedLockOptions"/> from the supplied <paramref name="configuration"/>
    /// section and registers FluentValidation-backed startup validation.
    /// Returns the same builder for chaining.
    /// </summary>
    /// <param name="configuration">
    /// The configuration section whose values are bound to <see cref="DistributedLockOptions"/>.
    /// </param>
    /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    public HeadlessDistributedLocksSetupBuilder ConfigureOptions(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        Services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(configuration);

        return this;
    }

    /// <summary>
    /// Configures <see cref="DistributedLockOptions"/> with an inline delegate and registers
    /// FluentValidation-backed startup validation.
    /// Returns the same builder for chaining.
    /// </summary>
    /// <param name="configure">Delegate that mutates the options instance.</param>
    /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public HeadlessDistributedLocksSetupBuilder ConfigureOptions(Action<DistributedLockOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(configure);

        return this;
    }

    /// <summary>
    /// Configures <see cref="DistributedLockOptions"/> with a delegate that receives both the
    /// options instance and the resolved <see cref="IServiceProvider"/>, and registers
    /// FluentValidation-backed startup validation.
    /// Returns the same builder for chaining.
    /// </summary>
    /// <param name="configure">
    /// Delegate that mutates the options instance using resolved services.
    /// </param>
    /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public HeadlessDistributedLocksSetupBuilder ConfigureOptions(
        Action<DistributedLockOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        Services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(configure);

        return this;
    }

    /// <summary>
    /// Registers a backend provider extension. Exactly one extension must be registered;
    /// <see cref="SetupDistributedLocks"/> enforces this constraint at setup time.
    /// Provider packages call this method from their <c>Use*</c> builder extensions — callers
    /// should not need to call it directly.
    /// </summary>
    /// <param name="extension">The provider extension to register.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="extension"/> is <see langword="null"/>.
    /// </exception>
    public void RegisterExtension(IDistributedLocksOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
