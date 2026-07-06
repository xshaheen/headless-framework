// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Service-collection extensions that register the Stripe-style HTTP idempotency middleware and
/// its supporting options/validators. Pair with <c>UseIdempotency()</c> on the application
/// pipeline.
/// </summary>
[PublicAPI]
public static class SetupIdempotency
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the idempotency middleware and binds <see cref="IdempotencyOptions"/> from
        /// the supplied <see cref="IConfiguration"/> section. Options are validated via
        /// FluentValidation with <c>ValidateOnStart()</c>.
        /// </summary>
        /// <param name="configuration">
        /// The configuration section to bind to <see cref="IdempotencyOptions"/>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <remarks>
        /// Call <c>UseIdempotency()</c> on the application builder to activate the middleware.
        /// When <see cref="IdempotencyOptions.InFlightStrategy"/> is
        /// <see cref="InFlightStrategy.WaitAndReplay"/>, an <c>IDistributedLock</c> must also be
        /// registered — the DI validator enforces this at host startup and raises
        /// <see cref="OptionsValidationException"/> if it is absent.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup (during <c>ValidateOnStart()</c>) when
        /// <see cref="IdempotencyOptions"/> fails FluentValidation rules or when
        /// <see cref="InFlightStrategy.WaitAndReplay"/> is selected but no
        /// <c>IDistributedLock</c> is registered.
        /// </exception>
        public IServiceCollection AddIdempotency(IConfiguration configuration)
        {
            services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(configuration);
            return services._AddIdempotencyCore();
        }

        /// <summary>
        /// Registers the idempotency middleware and configures <see cref="IdempotencyOptions"/>
        /// via the supplied delegate. Options are validated via FluentValidation with
        /// <c>ValidateOnStart()</c>.
        /// </summary>
        /// <param name="setupAction">Delegate that configures <see cref="IdempotencyOptions"/>.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <remarks>
        /// Call <c>UseIdempotency()</c> on the application builder to activate the middleware.
        /// When <see cref="IdempotencyOptions.InFlightStrategy"/> is
        /// <see cref="InFlightStrategy.WaitAndReplay"/>, an <c>IDistributedLock</c> must also be
        /// registered — the DI validator enforces this at host startup and raises
        /// <see cref="OptionsValidationException"/> if it is absent.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="setupAction"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup (during <c>ValidateOnStart()</c>) when
        /// <see cref="IdempotencyOptions"/> fails FluentValidation rules or when
        /// <see cref="InFlightStrategy.WaitAndReplay"/> is selected but no
        /// <c>IDistributedLock</c> is registered.
        /// </exception>
        public IServiceCollection AddIdempotency(Action<IdempotencyOptions> setupAction)
        {
            services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(setupAction);
            return services._AddIdempotencyCore();
        }

        /// <summary>
        /// Registers the idempotency middleware and configures <see cref="IdempotencyOptions"/>
        /// via the supplied delegate, which receives the resolved <see cref="IServiceProvider"/>
        /// for dependency lookups. Options are validated via FluentValidation with
        /// <c>ValidateOnStart()</c>.
        /// </summary>
        /// <param name="setupAction">
        /// Delegate that configures <see cref="IdempotencyOptions"/> with access to the
        /// <see cref="IServiceProvider"/> for resolving additional services.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <remarks>
        /// Call <c>UseIdempotency()</c> on the application builder to activate the middleware.
        /// When <see cref="IdempotencyOptions.InFlightStrategy"/> is
        /// <see cref="InFlightStrategy.WaitAndReplay"/>, an <c>IDistributedLock</c> must also be
        /// registered — the DI validator enforces this at host startup and raises
        /// <see cref="OptionsValidationException"/> if it is absent.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="setupAction"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup (during <c>ValidateOnStart()</c>) when
        /// <see cref="IdempotencyOptions"/> fails FluentValidation rules or when
        /// <see cref="InFlightStrategy.WaitAndReplay"/> is selected but no
        /// <c>IDistributedLock</c> is registered.
        /// </exception>
        public IServiceCollection AddIdempotency(Action<IdempotencyOptions, IServiceProvider> setupAction)
        {
            services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(setupAction);
            return services._AddIdempotencyCore();
        }

        private IServiceCollection _AddIdempotencyCore()
        {
            services.TryAddScoped<IdempotencyMiddleware>();

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsDiValidator>()
            );

            return services;
        }
    }
}
