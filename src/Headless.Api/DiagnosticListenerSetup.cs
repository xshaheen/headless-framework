// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Api.Diagnostics;
using Headless.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

[PublicAPI]
public static class DiagnosticListenerSetup
{
    extension(WebApplication app)
    {
        [MustDisposeResource]
        public IDisposable AddApiBadRequestDiagnosticListeners()
        {
            var diagnosticListener = app.Services.GetRequiredService<DiagnosticListener>();
            var badRequest = new BadRequestDiagnosticAdapter(app.Logger);
            var badRequestSubscription = diagnosticListener.SubscribeWithAdapter(badRequest);

            return badRequestSubscription;
        }

        [MustDisposeResource]
        public IDisposable AddMiddlewareAnalysisDiagnosticListeners()
        {
            var diagnosticListener = app.Services.GetRequiredService<DiagnosticListener>();
            var middlewareAnalysis = new MiddlewareAnalysisDiagnosticAdapter(app.Logger);
            var middlewareAnalysisSubscription = diagnosticListener.SubscribeWithAdapter(middlewareAnalysis);

            return middlewareAnalysisSubscription;
        }

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
