// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Validation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IStartupValidator" /> checks that run before any hosted service starts.</summary>
[PublicAPI]
public static class HeadlessStartupValidatorExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers <typeparamref name="TValidator" /> as a singleton startup check.</summary>
        /// <typeparam name="TValidator">The validator type, constructed by the container.</typeparam>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>Idempotent per validator type: a second call for the same type adds nothing.</remarks>
        public IServiceCollection AddStartupValidator<TValidator>()
            where TValidator : class, IStartupValidator
        {
            return services.AddStartupValidator(typeof(TValidator));
        }

        /// <summary>Registers <paramref name="validatorType" /> as a singleton startup check.</summary>
        /// <param name="validatorType">
        /// A concrete type implementing <see cref="IStartupValidator" />, such as a closed generic built with
        /// <see cref="Type.MakeGenericType" /> for a consumer's <c>DbContext</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>Idempotent per validator type: a second call for the same type adds nothing.</remarks>
        /// <exception cref="ArgumentException"><paramref name="validatorType" /> does not implement <see cref="IStartupValidator" />.</exception>
        public IServiceCollection AddStartupValidator(Type validatorType)
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(validatorType);

            if (!typeof(IStartupValidator).IsAssignableFrom(validatorType))
            {
                throw new ArgumentException(
                    $"{validatorType.FullName} does not implement {nameof(IStartupValidator)}.",
                    nameof(validatorType)
                );
            }

            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IStartupValidator), validatorType));

            return services._AddStartupValidationRunner();
        }

        /// <summary>Registers a startup check built by <paramref name="factory" />.</summary>
        /// <param name="factory">Builds the validator from the container.</param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>
        /// Every call adds a validator, which suits one validator per named instance of a feature. Use the typed
        /// overloads when repeated registration calls must collapse to one check.
        /// </remarks>
        public IServiceCollection AddStartupValidator(Func<IServiceProvider, IStartupValidator> factory)
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(factory);

            services.AddSingleton(factory);

            return services._AddStartupValidationRunner();
        }

        private IServiceCollection _AddStartupValidationRunner()
        {
            // Idempotent by implementation type, so one runner serves every validator in the host.
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StartupValidationRunner>());

            return services;
        }
    }
}
