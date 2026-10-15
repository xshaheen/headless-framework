// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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
        /// The registration name, unique in the host. A second contribution with the same name adds no second check: its
        /// tags join the first one's, so a provider registered twice keeps one check, and two packages that probe one
        /// dependency under one name report it once with both packages' tags.
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
            return _AddProbeCheck(services, name, resource: null, probe, tags);
        }

        /// <summary>
        /// Contributes a readiness health check that runs <paramref name="probe" /> against
        /// <paramref name="resource" />, collapsing into one check with every other contribution that probes the same
        /// resource (see <see cref="HeadlessHealthCheckResource" />).
        /// </summary>
        /// <param name="name">The registration name, unique in the host.</param>
        /// <param name="resource">The dependency the probe reaches, which decides what the check collapses with.</param>
        /// <param name="probe">Probes the dependency; completing is healthy and throwing reports the failure status.</param>
        /// <param name="tags">Tags added to <see cref="HeadlessHealthCheckTags.Ready" /> and <see cref="HeadlessHealthCheckTags.Headless" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="resource" /> or <paramref name="probe" /> is <see langword="null" />.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is <see langword="null" />, empty, or whitespace.</exception>
        public IServiceCollection AddHeadlessHealthCheck(
            string name,
            HeadlessHealthCheckResource resource,
            Func<IServiceProvider, CancellationToken, Task> probe,
            params IEnumerable<string> tags
        )
        {
            Argument.IsNotNull(resource);

            return _AddProbeCheck(services, name, resource, probe, tags);
        }

        /// <summary>
        /// Contributes a readiness health check that a third-party registration method adds, such as
        /// <c>AddDbContextCheck&lt;TContext&gt;</c>, under the same conventions as the probe overload: the
        /// <see cref="HeadlessHealthCheckTags.Ready" /> and <see cref="HeadlessHealthCheckTags.Headless" /> tags, one
        /// registration per name, and failures reported with the registration's failure status and fixed description text.
        /// </summary>
        /// <param name="name">
        /// The registration name, unique in the host. A second contribution with the same name adds only its tags.
        /// </param>
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
            return _AddRegisteredCheck(services, name, resource: null, register, tags);
        }

        /// <summary>
        /// Contributes a readiness health check that a third-party registration method adds for
        /// <paramref name="resource" />, collapsing into one check with every other contribution that probes the same
        /// resource (see <see cref="HeadlessHealthCheckResource" />).
        /// </summary>
        /// <param name="name">The registration name, unique in the host.</param>
        /// <param name="resource">The dependency the check reaches, which decides what the check collapses with.</param>
        /// <param name="register">Adds the check to the builder under the name and tags it receives.</param>
        /// <param name="tags">Tags added to <see cref="HeadlessHealthCheckTags.Ready" /> and <see cref="HeadlessHealthCheckTags.Headless" />.</param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="resource" /> or <paramref name="register" /> is <see langword="null" />.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is <see langword="null" />, empty, or whitespace.</exception>
        public IServiceCollection AddHeadlessHealthCheck(
            string name,
            HeadlessHealthCheckResource resource,
            Action<IHealthChecksBuilder, string, IReadOnlyList<string>> register,
            params IEnumerable<string> tags
        )
        {
            Argument.IsNotNull(resource);

            return _AddRegisteredCheck(services, name, resource, register, tags);
        }

        /// <summary>
        /// Configures the health check a Headless provider package contributes under <paramref name="name" />: whether it
        /// is registered, its failure status, timeout, extra tags, and test command (see
        /// <see cref="HeadlessHealthCheckOptions" />).
        /// </summary>
        /// <param name="name">The contributed check's name, such as <c>sql-postgresql</c> or <c>cache-redis</c>.</param>
        /// <param name="configure">Changes the options.</param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>
        /// The options are named options, validated at startup. Calling this before or after the provider's
        /// registration has the same effect, and it wins over the <c>Headless:HealthChecks:{name}</c> configuration
        /// section. A name no provider contributes changes nothing.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is <see langword="null" />, empty, or whitespace.</exception>
        public IServiceCollection ConfigureHeadlessHealthCheck(
            string name,
            Action<HeadlessHealthCheckOptions> configure
        )
        {
            Argument.IsNotNull(services);
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(configure);

            return services.Configure<HeadlessHealthCheckOptions, HeadlessHealthCheckOptionsValidator>(configure, name);
        }

        /// <summary>
        /// Sets a provider package's default for the health check it contributes under <paramref name="name" />, such as
        /// a <see cref="HealthStatus.Degraded" /> failure status for a check whose failure leaves the host able to work.
        /// </summary>
        /// <param name="name">The contributed check's name.</param>
        /// <param name="configure">Changes the options.</param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <remarks>
        /// A default is applied before the <c>Headless:HealthChecks:{name}</c> configuration section and every
        /// <see cref="ConfigureHeadlessHealthCheck" /> call, whatever order they are registered in, so an application
        /// always overrides it. Applications call <see cref="ConfigureHeadlessHealthCheck" /> instead.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is <see langword="null" />, empty, or whitespace.</exception>
        public IServiceCollection ConfigureHeadlessHealthCheckDefault(
            string name,
            Action<HeadlessHealthCheckOptions> configure
        )
        {
            Argument.IsNotNull(services);
            Argument.IsNotNullOrWhiteSpace(name);
            Argument.IsNotNull(configure);

            // Registers the configuration source that applies defaults, even before any check is contributed.
            _GetNames(services);
            var defaults = _GetDefaults(services);

            if (!defaults.TryGetValue(name, out var configureDefaults))
            {
                defaults[name] = configureDefaults = [];
            }

            configureDefaults.Add(configure);

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

    private static IServiceCollection _AddProbeCheck(
        IServiceCollection services,
        string name,
        HeadlessHealthCheckResource? resource,
        Func<IServiceProvider, CancellationToken, Task> probe,
        IEnumerable<string> tags
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(probe);
        Argument.IsNotNull(tags);

        if (!_GetNames(services).Add(name))
        {
            _MergeTags(services, name, tags);

            return services;
        }

        if (resource is not null)
        {
            _GetResources(services)[name] = resource;
        }

        string[] allTags = [HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Headless, .. tags];

        services.AddHealthChecks();
        services.Configure<HealthCheckServiceOptions>(options =>
        {
            // An application check registered under the same name first keeps the name.
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

        _ApplyOptions(services, name, sanitize: false);

        return services;
    }

    private static IServiceCollection _AddRegisteredCheck(
        IServiceCollection services,
        string name,
        HeadlessHealthCheckResource? resource,
        Action<IHealthChecksBuilder, string, IReadOnlyList<string>> register,
        IEnumerable<string> tags
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(register);
        Argument.IsNotNull(tags);

        // Tracked at registration time: the third-party method adds its registration unconditionally, and the
        // health check service rejects two registrations with one name.
        if (!_GetNames(services).Add(name))
        {
            _MergeTags(services, name, tags);

            return services;
        }

        if (resource is not null)
        {
            _GetResources(services)[name] = resource;
        }

        string[] allTags = [HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Headless, .. tags];

        register(services.AddHealthChecks(), name, allTags);

        // Runs after the registration method's own Configure call, so the registration exists to adjust.
        _ApplyOptions(services, name, sanitize: true);

        return services;
    }

    private static HeadlessHealthCheckDefaults _GetDefaults(IServiceCollection services)
    {
        if (
            services
                .FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(HeadlessHealthCheckDefaults))
                ?.ImplementationInstance
            is not HeadlessHealthCheckDefaults defaults
        )
        {
            defaults = new HeadlessHealthCheckDefaults();
            services.AddSingleton(defaults);
        }

        return defaults;
    }

    private static HeadlessHealthCheckResources _GetResources(IServiceCollection services)
    {
        if (
            services
                .FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(HeadlessHealthCheckResources))
                ?.ImplementationInstance
            is not HeadlessHealthCheckResources resources
        )
        {
            resources = new HeadlessHealthCheckResources();
            services.AddSingleton(resources);

            // A post-configuration, so it sees each check after its options turned it off or retuned it.
            services
                .AddOptions<HealthCheckServiceOptions>()
                .PostConfigure<IServiceProvider>(
                    (options, provider) => _Collapse(options.Registrations, resources, provider)
                );
        }

        return resources;
    }

    /// <summary>
    /// Folds the checks that probe one dependency into one: the owner's, else the first registered, which takes the
    /// others' tags.
    /// </summary>
    private static void _Collapse(
        ICollection<HealthCheckRegistration> registrations,
        HeadlessHealthCheckResources resources,
        IServiceProvider provider
    )
    {
        var keyed = new List<(HealthCheckRegistration Registration, string Key, bool Owns)>();

        foreach (var registration in registrations)
        {
            if (resources.TryGetValue(registration.Name, out var resource) && _Key(resource, provider) is { } key)
            {
                keyed.Add((registration, key, resource.OwnsConnection));
            }
        }

        foreach (
            var group in keyed.GroupBy(static x => x.Key, StringComparer.Ordinal).Where(static x => x.Skip(1).Any())
        )
        {
            var survivor = group.FirstOrDefault(static x => x.Owns).Registration ?? group.First().Registration;

            foreach (var (registration, _, _) in group)
            {
                if (ReferenceEquals(registration, survivor))
                {
                    continue;
                }

                survivor.Tags.UnionWith(registration.Tags);
                registrations.Remove(registration);
            }
        }
    }

    // A key that cannot be computed leaves its check alone rather than failing every health request.
    private static string? _Key(HeadlessHealthCheckResource resource, IServiceProvider provider)
    {
        try
        {
            return resource.Key(provider);
        }
#pragma warning disable CA1031, ERP022 // False positive: a dependency the key cannot describe is simply not deduplicated; the check itself still runs and reports any real failure.
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031, ERP022
    }

    private static HeadlessHealthCheckNames _GetNames(IServiceCollection services)
    {
        if (
            services
                .FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(HeadlessHealthCheckNames))
                ?.ImplementationInstance
            is not HeadlessHealthCheckNames names
        )
        {
            names = new HeadlessHealthCheckNames();
            services.AddSingleton(names);

            // First in the collection, so every ConfigureHeadlessHealthCheck call runs after it and wins.
            var defaults = _GetDefaults(services);
            services.Insert(
                0,
                ServiceDescriptor.Singleton<IConfigureOptions<HeadlessHealthCheckOptions>>(
                    provider => new HeadlessHealthCheckConfiguration(defaults, provider.GetService<IConfiguration>())
                )
            );
        }

        return names;
    }

    /// <summary>
    /// Applies the check's <see cref="HeadlessHealthCheckOptions" /> to its registration, and wraps a third-party
    /// check so its failures follow the Headless reporting rules.
    /// </summary>
    private static void _ApplyOptions(IServiceCollection services, string name, bool sanitize)
    {
        services
            .AddOptions<HealthCheckServiceOptions>()
            .Configure<IOptionsMonitor<HeadlessHealthCheckOptions>>(
                (options, monitor) =>
                {
                    var settings = monitor.Get(name);

                    if (!settings.Enabled)
                    {
                        _Remove(options.Registrations, name);

                        return;
                    }

                    foreach (var registration in options.Registrations)
                    {
                        if (!string.Equals(registration.Name, name, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        registration.FailureStatus = settings.FailureStatus;

                        if (settings.Timeout is { } timeout)
                        {
                            registration.Timeout = timeout;
                        }

                        foreach (var tag in settings.Tags)
                        {
                            registration.Tags.Add(tag);
                        }

                        if (sanitize && registration.Factory.Target is not HeadlessSanitizedFactory)
                        {
                            registration.Factory = new HeadlessSanitizedFactory(registration.Factory).Create;
                        }
                    }
                }
            );
    }

    // Runs after the first contribution's own configuration, which added the registration these tags join.
    private static void _MergeTags(IServiceCollection services, string name, IEnumerable<string> tags)
    {
        string[] extra = [.. tags];

        if (extra.Length == 0)
        {
            return;
        }

        services.Configure<HealthCheckServiceOptions>(options =>
        {
            foreach (var registration in options.Registrations)
            {
                if (string.Equals(registration.Name, name, StringComparison.Ordinal))
                {
                    registration.Tags.UnionWith(extra);
                }
            }
        });
    }

    private static void _Remove(ICollection<HealthCheckRegistration> registrations, string name)
    {
        foreach (
            var registration in registrations
                .Where(registration => string.Equals(registration.Name, name, StringComparison.Ordinal))
                .ToList()
        )
        {
            registrations.Remove(registration);
        }
    }

    private sealed class HeadlessHealthCheckResources : Dictionary<string, HeadlessHealthCheckResource>
    {
        public HeadlessHealthCheckResources()
            : base(StringComparer.Ordinal) { }
    }

    private sealed class HeadlessHealthCheckNames : HashSet<string>
    {
        public HeadlessHealthCheckNames()
            : base(StringComparer.Ordinal) { }
    }

    private sealed class HeadlessSanitizedFactory(Func<IServiceProvider, IHealthCheck> inner)
    {
        public HeadlessSanitizedHealthCheck Create(IServiceProvider provider)
        {
            return new HeadlessSanitizedHealthCheck(() => inner(provider));
        }
    }
}
