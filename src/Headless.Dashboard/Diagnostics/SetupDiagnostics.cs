// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>Adds the built-in diagnostic module to the dashboard.</summary>
[PublicAPI]
public static class SetupDiagnostics
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the diagnostic module under <see cref="DiagnosticsOptions.DefaultId"/>: an in-process store of fake runs
        /// that exercises every module capability, for trying the dashboard and testing modules against it.
        /// </summary>
        /// <remarks>
        /// The module serves only in the Development environment, unless <see cref="DiagnosticsOptions.Enabled"/> is
        /// set; elsewhere this call registers nothing, so the module is absent from the dashboard and its routes
        /// answer <c>404</c>. It keeps its runs in memory and adds no table, cache key, or channel to the host.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        public HeadlessDashboardSetupBuilder AddDiagnostics()
        {
            return dashboard.AddDiagnostics(static _ => { });
        }

        /// <summary>
        /// Adds a diagnostic module instance configured by <paramref name="setupAction"/>, such as
        /// <c>d =&gt; d.Instance("diag-b")</c> for a second instance under another id.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The module serves only in the Development environment, unless <see cref="DiagnosticsOptions.Enabled"/> is
        /// set; elsewhere this call registers nothing, so the module is absent from the dashboard and its routes
        /// answer <c>404</c>. It keeps its runs in memory and adds no table, cache key, or channel to the host.
        /// </para>
        /// <para>
        /// <paramref name="setupAction"/> runs once now, to read the id, <see cref="DiagnosticsOptions.Enabled"/>, and
        /// the store, which decide what is registered, and again when the options system builds the instance's
        /// options. No overload takes the service provider, because those settings are needed before it exists.
        /// </para>
        /// </remarks>
        /// <param name="setupAction">Configures the instance.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The id is not a lower-case kebab token, or is reserved.</exception>
        /// <exception cref="InvalidOperationException">A module with the same id is already added.</exception>
        public HeadlessDashboardSetupBuilder AddDiagnostics(Action<DiagnosticsOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var settings = new DiagnosticsOptions();
            setupAction(settings);

            return _Add(
                dashboard,
                settings,
                static (services, id, setup) =>
                    services.Configure<DiagnosticsOptions, DiagnosticsOptionsValidator>(setup, id),
                setupAction
            );
        }

        /// <summary>
        /// Adds a diagnostic module instance bound from <paramref name="configuration"/>, with the keys <c>Id</c>,
        /// <c>Enabled</c>, <c>ViewPolicy</c>, and <c>OperatePolicy</c>.
        /// </summary>
        /// <remarks>
        /// The module serves only in the Development environment, unless <c>Enabled</c> is <see langword="true"/>;
        /// elsewhere this call registers nothing, so the module is absent from the dashboard and its routes answer
        /// <c>404</c>. The section is read once now, to decide what is registered, and bound again by the options
        /// system. A shared store cannot come from configuration; use the delegate overload's
        /// <see cref="DiagnosticsOptions.UseStore"/>.
        /// </remarks>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The id is not a lower-case kebab token, or is reserved.</exception>
        /// <exception cref="InvalidOperationException">A module with the same id is already added.</exception>
        public HeadlessDashboardSetupBuilder AddDiagnostics(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var settings = new DiagnosticsOptions();
            configuration.Bind(settings);

            return _Add(
                dashboard,
                settings,
                static (services, id, section) =>
                    services.Configure<DiagnosticsOptions, DiagnosticsOptionsValidator>(section, id),
                configuration
            );
        }
    }

    private static HeadlessDashboardSetupBuilder _Add<TSource>(
        HeadlessDashboardSetupBuilder dashboard,
        DiagnosticsOptions settings,
        Action<IServiceCollection, string, TSource> configureOptions,
        TSource source
    )
    {
        var services = dashboard.Services;

        if (!settings.Enabled && !_IsDevelopment(services))
        {
            return dashboard;
        }

        var id = settings.Id;
        var sharedStore = settings.Store;

        configureOptions(services, id, source);

        services.AddKeyedSingleton(
            id,
            (provider, _) => sharedStore ?? new DiagnosticsStore(provider.GetRequiredService<TimeProvider>())
        );
        services.AddKeyedSingleton(
            id,
            (provider, _) =>
                new DiagnosticsModule(
                    id,
                    provider.GetRequiredKeyedService<DiagnosticsStore>(id),
                    provider.GetRequiredService<IDashboardEventPublisher>(),
                    provider.GetRequiredService<TimeProvider>()
                )
        );

        // AddModule registers the module type itself when nothing has; a factory registered first keeps it from adding
        // one the container cannot build, since the module's id is not a service.
        services.TryAddSingleton(provider => provider.GetRequiredKeyedService<DiagnosticsModule>(id));

        dashboard.AddModule<DiagnosticsModule, DiagnosticsOptions>(id);

        // Several instances share one module and options type, so each instance resolves its own keyed module and
        // named options instead of the type's single registration.
        var index = dashboard.Modules.Count - 1;
        dashboard.Modules[index] = dashboard.Modules[index] with
        {
            ResolveModule = provider => provider.GetRequiredKeyedService<DiagnosticsModule>(id),
            ResolveOptions = provider => provider.GetRequiredService<IOptionsMonitor<DiagnosticsOptions>>().Get(id),
        };

        return dashboard;
    }

    // Registration runs before the container exists, so the environment is read from the instance the host builder
    // registered. A host that registers none is treated as not Development.
    private static bool _IsDevelopment(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];

            if (descriptor.ServiceType == typeof(IHostEnvironment) && !descriptor.IsKeyedService)
            {
                return descriptor.ImplementationInstance is IHostEnvironment environment && environment.IsDevelopment();
            }
        }

        return false;
    }
}
