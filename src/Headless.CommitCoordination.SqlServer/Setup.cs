// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.CommitCoordination;

/// <summary>
/// Registers SQL Server commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupSqlServerCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds SQL Server commit coordination services with default
        /// <see cref="SqlServerCommitCoordinationOptions" />: the core infrastructure, the out-of-band SqlClient
        /// diagnostic observer, the commit signal source, the startup diagnostic self-probe, and the
        /// <see cref="SqlServerCommitDiagnosticProbeState" /> singleton.
        /// </summary>
        /// <remarks>
        /// SQL Server commit detection is out-of-band: a <c>DiagnosticListener</c> subscription on the SqlClient
        /// diagnostic source fires after each native commit or rollback. The hosted service
        /// (<c>SqlServerCommitDiagnosticHostedService</c>) subscribes at startup and unsubscribes on stop.
        /// The startup self-probe verifies the diagnostic listener is firing; configure its behavior through
        /// <see cref="SqlServerCommitCoordinationOptions" /> (probe mode, connection factory, timeout) using one of
        /// the configuring overloads. Idempotent: repeated calls register each service at most once.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddSqlServerCommitCoordination()
        {
            services.AddOptions<SqlServerCommitCoordinationOptions, SqlServerCommitCoordinationOptionsValidator>();

            return _AddSqlServerCommitCoordinationCore(services);
        }

        /// <summary>
        /// Adds SQL Server commit coordination services, binding
        /// <see cref="SqlServerCommitCoordinationOptions" /> from the supplied <see cref="IConfiguration" /> section.
        /// </summary>
        /// <remarks>
        /// See the parameterless <c>AddSqlServerCommitCoordination()</c> overload for the registered services and the out-of-band
        /// detection model. Idempotent: repeated calls register each service at most once.
        /// </remarks>
        /// <param name="configuration">The configuration section to bind <see cref="SqlServerCommitCoordinationOptions" /> from.</param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public IServiceCollection AddSqlServerCommitCoordination(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            services.Configure<SqlServerCommitCoordinationOptions, SqlServerCommitCoordinationOptionsValidator>(
                configuration
            );

            return _AddSqlServerCommitCoordinationCore(services);
        }

        /// <summary>
        /// Adds SQL Server commit coordination services, configuring
        /// <see cref="SqlServerCommitCoordinationOptions" /> with the supplied delegate.
        /// </summary>
        /// <remarks>
        /// See the parameterless <c>AddSqlServerCommitCoordination()</c> overload for the registered services and the out-of-band
        /// detection model. Idempotent: repeated calls register each service at most once.
        /// </remarks>
        /// <param name="configure">
        /// Delegate that configures <see cref="SqlServerCommitCoordinationOptions" /> (probe mode, connection
        /// factory, timeout).
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddSqlServerCommitCoordination(Action<SqlServerCommitCoordinationOptions> configure)
        {
            Argument.IsNotNull(configure);

            services.Configure<SqlServerCommitCoordinationOptions, SqlServerCommitCoordinationOptionsValidator>(
                configure
            );

            return _AddSqlServerCommitCoordinationCore(services);
        }

        /// <summary>
        /// Adds SQL Server commit coordination services, configuring
        /// <see cref="SqlServerCommitCoordinationOptions" /> with the supplied delegate with access to the DI
        /// container.
        /// </summary>
        /// <remarks>
        /// See the parameterless <c>AddSqlServerCommitCoordination()</c> overload for the registered services and the out-of-band
        /// detection model. Idempotent: repeated calls register each service at most once.
        /// </remarks>
        /// <param name="configure">
        /// Delegate that configures <see cref="SqlServerCommitCoordinationOptions" /> with service-provider access.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public IServiceCollection AddSqlServerCommitCoordination(
            Action<SqlServerCommitCoordinationOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            services.Configure<SqlServerCommitCoordinationOptions, SqlServerCommitCoordinationOptionsValidator>(
                configure
            );

            return _AddSqlServerCommitCoordinationCore(services);
        }
    }

    private static IServiceCollection _AddSqlServerCommitCoordinationCore(IServiceCollection services)
    {
        services.AddCommitCoordination();

        services.TryAddSingleton<SqlServerCommitDiagnosticProbeState>();
        services.TryAddSingleton<ISqlServerCommitDiagnosticProbe, SqlServerCommitDiagnosticProbe>();
        services.TryAddSingleton<SqlServerCommitSignalSource>();
        services.TryAddSingleton<ICommitSignalSource>(sp => sp.GetRequiredService<SqlServerCommitSignalSource>());
        services.TryAddSingleton<SqlServerCommitDiagnosticObserver>();
        services.TryAddSingleton<SqlServerCommitDiagnosticListenerObserver>();

        // The hosted service owns the SqlClientDiagnosticListener subscription lifetime (subscribe on start,
        // dispose on stop), so the out-of-band commit/rollback detection observes real provider edges.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, SqlServerCommitDiagnosticHostedService>()
        );

        return services;
    }
}
