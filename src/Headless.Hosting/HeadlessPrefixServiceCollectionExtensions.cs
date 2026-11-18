// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Sets the host's isolation prefix (<see cref="HeadlessPrefixOptions" />) in code, and lets a feature apply it to the
/// names its options carry.
/// </summary>
[PublicAPI]
public static class HeadlessPrefixServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Sets the host's isolation prefix, which every Headless feature puts in front of the names it writes to a
        /// shared store. It wins over <see cref="HeadlessPrefixOptions.ConfigurationKey" /> in configuration, whether
        /// it is called before or after the features are registered.
        /// </summary>
        /// <param name="prefix">
        /// Empty for no prefix, or 1 to <see cref="HeadlessPrefixOptions.MaxLength" /> lowercase letters, digits, and
        /// <c>-</c>, neither starting nor ending with <c>-</c>; any other value fails the host's start.
        /// </param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="prefix" /> is <see langword="null" />.</exception>
        public IServiceCollection ConfigureHeadlessPrefix(string prefix)
        {
            Argument.IsNotNull(prefix);

            services.AddHeadlessPrefix();
            services.Configure<HeadlessPrefixOptions>(options => options.Prefix = prefix);

            return services;
        }

        /// <summary>
        /// Registers <see cref="HeadlessPrefixOptions" />, bound from <see cref="HeadlessPrefixOptions.ConfigurationKey" />,
        /// validated when the host starts, and logged once at start. Feature registrations call it; repeated calls
        /// add nothing.
        /// </summary>
        /// <returns>The same service collection.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public IServiceCollection AddHeadlessPrefix()
        {
            if (services.Any(static descriptor => descriptor.ServiceType == typeof(HeadlessPrefixRegistration)))
            {
                return services;
            }

            services.AddSingleton(new HeadlessPrefixRegistration());

            // First in the collection, so every ConfigureHeadlessPrefix call runs after it and wins.
            services.Insert(
                0,
                ServiceDescriptor.Singleton<IConfigureOptions<HeadlessPrefixOptions>>(
                    static provider => new HeadlessPrefixConfiguration(provider.GetService<IConfiguration>())
                )
            );

            services.AddOptions<HeadlessPrefixOptions, HeadlessPrefixOptionsValidator>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HeadlessPrefixStartupLog>());

            return services;
        }

        /// <summary>
        /// Applies the host's isolation prefix to every named and unnamed instance of <typeparamref name="TOptions" />
        /// after all other configuration, once however many times it is called. A provider calls it from its
        /// registration for the options that hold the names it writes.
        /// </summary>
        /// <typeparam name="TOptions">The options type whose names the prefix applies to.</typeparam>
        /// <param name="apply">
        /// Puts the prefix, which is never empty here, in front of the options' names, for example
        /// <c>(options, prefix) =&gt; options.KeyPrefix = prefix + ":" + options.KeyPrefix</c>. It does not run when the
        /// host has no prefix.
        /// </param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="apply" /> is <see langword="null" />.</exception>
        /// <remarks>
        /// A provider registration runs once per default and named instance; applying the prefix from each would put it
        /// in front of every instance's names that many times. The first call for <typeparamref name="TOptions" />
        /// wins, and later calls add nothing.
        /// </remarks>
        public IServiceCollection ApplyHeadlessPrefix<TOptions>(Action<TOptions, string> apply)
            where TOptions : class
        {
            Argument.IsNotNull(apply);

            services.AddHeadlessPrefix();

            if (services.Any(static descriptor => descriptor.ServiceType == typeof(HeadlessPrefixApplied<TOptions>)))
            {
                return services;
            }

            services.AddSingleton(new HeadlessPrefixApplied<TOptions>());
            services.AddSingleton<IPostConfigureOptions<TOptions>>(provider => new PostConfigureOptions<
                TOptions,
                IOptions<HeadlessPrefixOptions>
            >(
                name: null,
                provider.GetRequiredService<IOptions<HeadlessPrefixOptions>>(),
                (options, prefix) =>
                {
                    if (prefix.Value.Prefix.Length > 0)
                    {
                        apply(options, prefix.Value.Prefix);
                    }
                }
            ));

            return services;
        }
    }

    private sealed class HeadlessPrefixRegistration;

    private sealed class HeadlessPrefixApplied<TOptions>;
}
