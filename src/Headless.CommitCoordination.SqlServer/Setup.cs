// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination.SqlServer;
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
        /// Adds SQL Server commit coordination services: the core infrastructure, the out-of-band SqlClient
        /// diagnostic observer, the <see cref="SqlServerCommitSignalSource" />, the startup diagnostic self-probe,
        /// and the <see cref="SqlServerCommitDiagnosticProbeState" /> singleton.
        /// </summary>
        /// <remarks>
        /// SQL Server commit detection is out-of-band: a <c>DiagnosticListener</c> subscription on the SqlClient
        /// diagnostic source fires after each native commit or rollback. The hosted service
        /// (<c>SqlServerCommitDiagnosticHostedService</c>) subscribes at startup and unsubscribes on stop.
        /// The startup self-probe verifies the diagnostic listener is firing; configure its behavior via
        /// <paramref name="configure" />. Idempotent: repeated calls register each service at most once.
        /// </remarks>
        /// <param name="configure">
        /// Optional delegate to configure <see cref="SqlServerCommitCoordinationOptions" /> (probe mode,
        /// connection factory, timeout). When <see langword="null" />, defaults are used.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddSqlServerCommitCoordination(
            Action<SqlServerCommitCoordinationOptions>? configure = null
        )
        {
            services.AddCommitCoordination();

            if (configure is not null)
            {
                services.Configure<SqlServerCommitCoordinationOptions, SqlServerCommitCoordinationOptionsValidator>(
                    configure
                );
            }
            else
            {
                services.AddOptions<SqlServerCommitCoordinationOptions, SqlServerCommitCoordinationOptionsValidator>();
            }

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
}
