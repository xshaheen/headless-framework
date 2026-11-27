// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Headless.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.Idempotency;

public static class SetupIdempotencyDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Idempotency module to the unified dashboard under <see cref="IdempotencyDashboardOptions.ModuleId"/>,
        /// in the Platform product: the idempotency store's settings and a read-only lookup of one key's record
        /// metadata in an operator-chosen tenant.
        /// </summary>
        /// <remarks>
        /// The module reads through the <see cref="IIdempotencyInspector"/> <c>AddHeadlessIdempotency</c> registers. A host
        /// that adds it without calling <c>AddHeadlessIdempotency</c> fails to start with a message naming that call.
        /// Every principal the module's view policy admits reads any tenant's record metadata, never a stored body.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>idempotency</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddIdempotency()
        {
            return dashboard.AddIdempotency(static _ => { });
        }

        /// <summary>Adds the Idempotency module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>idempotency</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddIdempotency(Action<IdempotencyDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<IdempotencyDashboardOptions, IdempotencyDashboardOptionsValidator>(
                setupAction
            );

            return _AddIdempotencyModule(dashboard);
        }

        /// <summary>Adds the Idempotency module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>idempotency</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddIdempotency(
            Action<IdempotencyDashboardOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<IdempotencyDashboardOptions, IdempotencyDashboardOptionsValidator>(
                setupAction
            );

            return _AddIdempotencyModule(dashboard);
        }

        /// <summary>
        /// Adds the Idempotency module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c> and
        /// <c>OperatePolicy</c>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>idempotency</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddIdempotency(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<IdempotencyDashboardOptions, IdempotencyDashboardOptionsValidator>(
                configuration
            );

            return _AddIdempotencyModule(dashboard);
        }
    }

    private const string _RequiredBy = "the Idempotency dashboard module (AddIdempotency)";

    private const string _Remedy =
        "Call services.AddHeadlessIdempotency(...) to register the Headless idempotency store.";

    private static HeadlessDashboardSetupBuilder _AddIdempotencyModule(HeadlessDashboardSetupBuilder dashboard)
    {
        var services = dashboard.Services;

        // Every read goes through the inspector AddHeadlessIdempotency registers; without it nothing could serve a
        // request, so the host fails at start rather than on the first dashboard call.
        services.RequireRegisteredService<IIdempotencyInspector>(_RequiredBy, _Remedy);

        services.TryAddSingleton(provider => new IdempotencyModule(
            _Require<IIdempotencyInspector>(provider),
            _Require<ICurrentTenant>(provider),
            provider.GetRequiredService<ILogger<IdempotencyModule>>()
        ));

        return dashboard.AddModule<IdempotencyModule, IdempotencyDashboardOptions>(
            IdempotencyDashboardOptions.ModuleId
        );
    }

    // The shell may build the module while it maps endpoints, before startup validation reports the missing call, so
    // the module names it too.
    private static T _Require<T>(IServiceProvider provider)
        where T : class =>
        provider.GetService<T>()
        ?? throw new InvalidOperationException($"{_RequiredBy} needs the Headless idempotency store. {_Remedy}");
}
