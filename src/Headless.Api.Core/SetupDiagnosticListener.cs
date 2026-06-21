// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Api.Diagnostics;
using Headless.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="WebApplication"/> for subscribing Headless diagnostic adapters
/// to the process-wide <see cref="DiagnosticListener"/>.
/// </summary>
[PublicAPI]
public static class SetupDiagnosticListener
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Subscribes <see cref="BadRequestDiagnosticAdapter"/> to the process-wide
        /// <see cref="DiagnosticListener"/> so Kestrel bad-request events are logged.
        /// </summary>
        /// <returns>
        /// A disposable subscription. Dispose it when the application shuts down to stop receiving events.
        /// </returns>
        /// <exception cref="InvalidOperationException"><see cref="DiagnosticListener"/> is not registered in the service container.</exception>
        [MustDisposeResource]
        public IDisposable AddApiBadRequestDiagnosticListeners()
        {
            var diagnosticListener = app.Services.GetRequiredService<DiagnosticListener>();
            var badRequest = new BadRequestDiagnosticAdapter(app.Logger);
            var badRequestSubscription = diagnosticListener.SubscribeWithAdapter(badRequest);

            return badRequestSubscription;
        }

        /// <summary>
        /// Subscribes <see cref="MiddlewareAnalysisDiagnosticAdapter"/> to the process-wide
        /// <see cref="DiagnosticListener"/> so middleware start/finish/exception events are logged.
        /// Requires <see cref="AddMiddlewareAnalyzerFilterExtensions.AddMiddlewareAnalyzerFilter"/> to have been called.
        /// </summary>
        /// <returns>
        /// A disposable subscription. Dispose it when the application shuts down to stop receiving events.
        /// </returns>
        /// <exception cref="InvalidOperationException"><see cref="DiagnosticListener"/> is not registered in the service container.</exception>
        [MustDisposeResource]
        public IDisposable AddMiddlewareAnalysisDiagnosticListeners()
        {
            var diagnosticListener = app.Services.GetRequiredService<DiagnosticListener>();
            var middlewareAnalysis = new MiddlewareAnalysisDiagnosticAdapter(app.Logger);
            var middlewareAnalysisSubscription = diagnosticListener.SubscribeWithAdapter(middlewareAnalysis);

            return middlewareAnalysisSubscription;
        }

        /// <summary>
        /// Subscribes all Headless diagnostic adapters (bad-request and middleware analysis) to the
        /// process-wide <see cref="DiagnosticListener"/>. Composes the two individual subscriptions
        /// into a single disposable root.
        /// </summary>
        /// <returns>
        /// A composite disposable that disposes both subscriptions when disposed.
        /// Dispose it when the application shuts down.
        /// </returns>
        /// <exception cref="InvalidOperationException"><see cref="DiagnosticListener"/> is not registered in the service container.</exception>
        [MustDisposeResource]
        public IDisposable AddHeadlessApiDiagnosticListeners()
        {
            // Delegate to the per-listener registrations to keep one source of truth for each
            // adapter; compose the returned disposables into a single root disposable.
            var badRequestSubscription = app.AddApiBadRequestDiagnosticListeners();
            var middlewareAnalysisSubscription = app.AddMiddlewareAnalysisDiagnosticListeners();

            return DisposableFactory.Create(() =>
            {
                badRequestSubscription.Dispose();
                middlewareAnalysisSubscription.Dispose();
            });
        }
    }
}
