// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the dependency health checks that Headless provider packages contribute, and lets an application remove
/// any health check registration.
/// </summary>
[PublicAPI]
public static class HeadlessHealthCheckExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Contributes a readiness health check that runs <paramref name="probe" /> against an external dependency.
        /// Provider packages call this from their registration; applications rarely need it.
        /// </summary>
        /// <param name="name">
        /// The registration name, unique in the host. A second contribution with the same name adds nothing, so a
        /// provider registered twice keeps one check.
        /// </param>
        /// <param name="probe">
        /// Probes the dependency with the services of the health check's own scope and the probe's cancellation token.
        /// Completing reports <see cref="HealthStatus.Healthy" />; throwing reports the registration's failure status
        /// (<see cref="HealthStatus.Unhealthy" /> unless changed) with fixed description text and the exception attached.
        /// </param>
        /// <param name="tags">
        /// Tags added to <see cref="HeadlessHealthCheckTags.Ready" /> and <see cref="HeadlessHealthCheckTags.Headless" />,
        /// such as <see cref="HeadlessHealthCheckTags.Database" />.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>
        /// Registration resolves nothing and opens no connection: the probe runs only when a health check runs. The
        /// check is added through <see cref="HealthCheckServiceOptions" />, so it appears on every health endpoint
        /// whose predicate accepts it. Remove or change it with <see cref="RemoveHealthChecks" /> or a
        /// <c>PostConfigure&lt;HealthCheckServiceOptions&gt;</c> call.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="probe" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is <see langword="null" />, empty, or whitespace.</exception>
        public IServiceCollection AddHeadlessHealthCheck(
            string name,
            Func<IServiceProvider, CancellationToken, Task> probe,
            params IEnumerable<string> tags
        )
        {
            Argument.IsNotNull(services);
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(probe);
            Argument.IsNotNull(tags);

            string[] allTags = [HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Headless, .. tags];

            services.AddHealthChecks();
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                foreach (var registration in options.Registrations)
                {
                    if (string.Equals(registration.Name, name, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                options.Registrations.Add(
                    new HealthCheckRegistration(
                        name,
                        provider => new HeadlessProbeHealthCheck(provider, probe),
                        failureStatus: null,
                        allTags
                    )
                );
            });

            return services;
        }

        /// <summary>
        /// Contributes a readiness health check that a third-party registration method adds, such as
        /// <c>AddDbContextCheck&lt;TContext&gt;</c>, under the same conventions as the probe overload: the
        /// <see cref="HeadlessHealthCheckTags.Ready" /> and <see cref="HeadlessHealthCheckTags.Headless" /> tags, one
        /// registration per name, and failures reported with the registration's failure status and fixed description text.
        /// </summary>
        /// <param name="name">The registration name, unique in the host. A second contribution with the same name adds nothing.</param>
        /// <param name="register">
        /// Adds the check to the builder under the name and tags it receives. It runs at most once per name.
        /// </param>
        /// <param name="tags">Tags added to <see cref="HeadlessHealthCheckTags.Ready" /> and <see cref="HeadlessHealthCheckTags.Headless" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="register" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is <see langword="null" />, empty, or whitespace.</exception>
        public IServiceCollection AddHeadlessHealthCheck(
            string name,
            Action<IHealthChecksBuilder, string, IReadOnlyList<string>> register,
            params IEnumerable<string> tags
        )
        {
            Argument.IsNotNull(services);
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(register);
            Argument.IsNotNull(tags);

            // Tracked at registration time: the third-party method adds its registration unconditionally, and the
            // health check service rejects two registrations with one name.
            if (
                services
                    .FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(HeadlessHealthCheckNames))
                    ?.ImplementationInstance
                is not HeadlessHealthCheckNames names
            )
            {
                names = new HeadlessHealthCheckNames();
                services.AddSingleton(names);
            }

            if (!names.Add(name))
            {
                return services;
            }

            string[] allTags = [HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Headless, .. tags];

            register(services.AddHealthChecks(), name, allTags);

            // Runs after the registration method's own Configure call, so the registration exists to wrap.
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                foreach (var registration in options.Registrations)
                {
                    if (
                        string.Equals(registration.Name, name, StringComparison.Ordinal)
                        && registration.Factory.Target is not HeadlessSanitizedFactory
                    )
                    {
                        registration.Factory = new HeadlessSanitizedFactory(registration.Factory).Create;
                    }
                }
            });

            return services;
        }

        /// <summary>
        /// Removes every health check registration that matches <paramref name="predicate" />, including checks that
        /// Headless provider packages contribute.
        /// </summary>
        /// <param name="predicate">Selects the registrations to remove.</param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>
        /// The removal runs after every other configuration of <see cref="HealthCheckServiceOptions" />, so it also
        /// removes checks registered after this call. To drop every contributed check, match
        /// <see cref="HeadlessHealthCheckTags.Headless" />.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="predicate" /> is <see langword="null" />.</exception>
        public IServiceCollection RemoveHealthChecks(Func<HealthCheckRegistration, bool> predicate)
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(predicate);

            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                var registrations = options.Registrations;

                foreach (var registration in registrations.Where(predicate).ToList())
                {
                    registrations.Remove(registration);
                }
            });

            return services;
        }
    }

    private sealed class HeadlessHealthCheckNames : HashSet<string>
    {
        public HeadlessHealthCheckNames()
            : base(StringComparer.Ordinal) { }
    }

    private sealed class HeadlessSanitizedFactory(Func<IServiceProvider, IHealthCheck> inner)
    {
        public IHealthCheck Create(IServiceProvider provider)
        {
            return new HeadlessSanitizedHealthCheck(() => inner(provider));
        }
    }
}
