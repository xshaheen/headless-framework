// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        public IServiceCollection AddIdempotency(Action<IdempotencyOptions, IServiceProvider> setupAction)
        {
            services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(setupAction);
            return services._AddIdempotencyCore();
        }

        private IServiceCollection _AddIdempotencyCore()
        {
            services.TryAddScoped<IdempotencyMiddleware>();

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsDIValidator>()
            );

            return services;
        }
    }
}
