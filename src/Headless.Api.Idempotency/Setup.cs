// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Api.Idempotency;

/// <summary>
/// Service-collection extensions that register the Stripe-style HTTP idempotency middleware and
/// its supporting options/validators. Pair with <c>UseIdempotency()</c> on the application
/// pipeline.
/// </summary>
/// <remarks>
/// The middleware admits requests through the durable <see cref="IIdempotentOperations"/> store, so the host must
/// also call <c>AddHeadlessIdempotency(...)</c> with a provider (in-memory for a single instance, or relational). The
/// host fails at startup when the store is missing.
/// </remarks>
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
        /// <remarks>Call <c>UseIdempotency()</c> on the application builder to activate the middleware.</remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configuration"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup (during <c>ValidateOnStart()</c>) when
        /// <see cref="IdempotencyOptions"/> fails FluentValidation rules.
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
        /// <remarks>Call <c>UseIdempotency()</c> on the application builder to activate the middleware.</remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="setupAction"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup (during <c>ValidateOnStart()</c>) when
        /// <see cref="IdempotencyOptions"/> fails FluentValidation rules.
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
        /// <remarks>Call <c>UseIdempotency()</c> on the application builder to activate the middleware.</remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="setupAction"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup (during <c>ValidateOnStart()</c>) when
        /// <see cref="IdempotencyOptions"/> fails FluentValidation rules.
        /// </exception>
        public IServiceCollection AddIdempotency(Action<IdempotencyOptions, IServiceProvider> setupAction)
        {
            services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(setupAction);
            return services._AddIdempotencyCore();
        }

        private IServiceCollection _AddIdempotencyCore()
        {
            services.TryAddScoped<IdempotencyMiddleware>();
            services.TryAddSingleton(TimeProvider.System);

            // The middleware admits, completes, and releases through the durable store, and this package references
            // only its abstractions: the implementation ships in a provider package the host installs. Without it
            // every idempotent request would fail.
            services.RequireRegisteredService<IIdempotentOperations>(
                requiredBy: "Headless API idempotency",
                remedy: "Call AddHeadlessIdempotency(...) with a provider (UseInMemory / UsePostgreSql / UseSqlServer)."
            );

            return services;
        }
    }
}
