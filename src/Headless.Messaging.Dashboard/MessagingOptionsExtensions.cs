// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Configuration;
using Headless.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Dashboard;

public static class MessagingOptionsExtensions
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Enables the Messaging Dashboard UI, wiring up static-file serving, authentication, CORS,
        /// and all dashboard API endpoints. The dashboard is mounted at the path configured via
        /// <c>SetBasePath</c> (default: <c>/messaging</c>).
        /// </summary>
        /// <param name="configure">
        /// An action to configure the dashboard options. An authentication mode must be chosen explicitly
        /// (<c>WithBasicAuth</c>, <c>WithApiKey</c>, <c>WithHostAuthentication</c>, <c>WithCustomAuth</c>, or an
        /// explicit <c>WithNoAuth()</c> opt-out) or the host fails to start.
        /// </param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseDashboard(Action<MessagingDashboardOptionsBuilder> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new DashboardOptionsExtension(configure));

            return setup;
        }
    }
}
