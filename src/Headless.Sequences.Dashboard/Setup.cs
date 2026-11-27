// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sequences;

public static class SetupSequencesDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Sequences module to the unified dashboard under <see cref="SequencesDashboardOptions.ModuleId"/>, in
        /// the Platform product: a read-only list of every stored counter across host and tenant scopes, with its
        /// value, partitions, policy, last formatted number, and next reset.
        /// </summary>
        /// <remarks>
        /// The module reads the <see cref="ISequenceInspector"/> <c>AddHeadlessSequences</c> registered and never
        /// takes a number. A host that adds it without calling <c>AddHeadlessSequences</c> fails to start with a
        /// message naming that call.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>sequences</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddSequences()
        {
            return dashboard.AddSequences(static _ => { });
        }

        /// <summary>Adds the Sequences module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>sequences</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddSequences(Action<SequencesDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<SequencesDashboardOptions, SequencesDashboardOptionsValidator>(setupAction);

            return _AddSequencesModule(dashboard);
        }

        /// <summary>Adds the Sequences module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>sequences</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddSequences(
            Action<SequencesDashboardOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<SequencesDashboardOptions, SequencesDashboardOptionsValidator>(setupAction);

            return _AddSequencesModule(dashboard);
        }

        /// <summary>
        /// Adds the Sequences module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c> and
        /// <c>OperatePolicy</c>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>sequences</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddSequences(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<SequencesDashboardOptions, SequencesDashboardOptionsValidator>(configuration);

            return _AddSequencesModule(dashboard);
        }
    }

    private const string _RequiredBy = "the Sequences dashboard module (AddSequences)";
    private const string _Remedy = "Call services.AddHeadlessSequences(...) with a provider to register the inspector.";

    private static HeadlessDashboardSetupBuilder _AddSequencesModule(HeadlessDashboardSetupBuilder dashboard)
    {
        var services = dashboard.Services;

        // Every route reads through the inspector; without it nothing could serve a request, so the host fails at
        // start rather than on the first dashboard call.
        services.RequireRegisteredService<ISequenceInspector>(_RequiredBy, _Remedy);

        services.TryAddSingleton(provider => new SequencesModule(
            _Require<ISequenceInspector>(provider),
            provider.GetRequiredService<IOptionsMonitor<SequencesOptions>>(),
            provider.GetRequiredService<ILogger<SequencesEndpoints>>()
        ));

        return dashboard.AddModule<SequencesModule, SequencesDashboardOptions>(SequencesDashboardOptions.ModuleId);
    }

    // The shell may build the module while it maps endpoints, before startup validation reports the missing call, so
    // the module names it too.
    private static T _Require<T>(IServiceProvider provider)
        where T : class =>
        provider.GetService<T>()
        ?? throw new InvalidOperationException($"{_RequiredBy} needs the Headless sequence inspector. {_Remedy}");
}
