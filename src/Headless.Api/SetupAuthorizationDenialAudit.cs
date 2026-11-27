// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api;

/// <summary>Extension members on <see cref="IServiceCollection"/> for auditing authorization denials.</summary>
[PublicAPI]
public static class SetupAuthorizationDenialAudit
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Records an audit entry through <see cref="IAuditLogWriter{TContext}"/> each time the authorization
        /// middleware challenges or forbids a request, so failed access attempts leave a record.
        /// </summary>
        /// <typeparam name="TContext">The persistence context that owns the audit log table.</typeparam>
        /// <returns>The same service collection.</returns>
        /// <remarks>
        /// <para>
        /// Each entry has action <c>authorization.challenged</c> (no or rejected authentication) or
        /// <c>authorization.forbidden</c> (authenticated, but a requirement failed), <c>Success</c> set to
        /// <see langword="false"/>, and the actor, tenant, and correlation ID the writer stamps from the current
        /// request. Its new values hold <c>method</c>, <c>route</c> (the endpoint's route template, never the
        /// request path), and <c>policies</c> (the named policies on the endpoint). The request body, query, and
        /// headers are never recorded.
        /// </para>
        /// <para>
        /// The entry commits before the challenge or forbid response is written and is not cancelled when the
        /// client disconnects. A failed audit write is logged and the denial response proceeds unchanged. It decorates
        /// the registered <see cref="IPolicyEvaluator"/>, so a custom <see cref="IAuthorizationMiddlewareResultHandler"/>
        /// registered before or after it still sees every denial audited; an <see cref="IPolicyEvaluator"/> registered
        /// after it replaces it. Code that evaluates a policy itself through <see cref="IPolicyEvaluator"/> is audited
        /// too; evaluate through <see cref="IAuthorizationService"/> to check a policy without recording a denial.
        /// Calling it more than once has no further effect.
        /// </para>
        /// </remarks>
        public IServiceCollection AddHeadlessAuthorizationDenialAudit<TContext>()
        {
            if (services.IsAdded<AuthorizationDenialAuditMarker>())
            {
                return services;
            }

            services.AddSingleton(new AuthorizationDenialAuditMarker());
            services.AddLogging();

            // AddAuthorization registers the default evaluator with TryAdd, so registering it here first keeps one
            // registration whichever call runs first, and the decorator always has something to wrap.
            services.TryAddTransient<IPolicyEvaluator, PolicyEvaluator>();
            services.TryDecorateWith<IPolicyEvaluator, AuditingPolicyEvaluator<TContext>>();

            return services;
        }
    }

    private sealed class AuthorizationDenialAuditMarker;
}
