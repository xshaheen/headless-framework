// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Per-request feature set by <c>TenantRequirementHandler</c> when authorization fails because
/// no tenant is resolved. <c>StatusCodesRewriterMiddleware</c> reads this feature on 403 responses
/// to substitute the structured <c>g:tenant_required</c> ProblemDetails body for the generic
/// Forbidden body. Typed features are keyed by .NET type, so this is not subject to the string-key
/// collision risk of <c>HttpContext.Items</c>.
/// </summary>
/// <remarks>
/// Set only on HTTP authorization contexts (where <c>AuthorizationHandlerContext.Resource</c> is
/// an <c>HttpContext</c>). For non-HTTP transports (SignalR, gRPC, programmatic
/// <c>IAuthorizationService.AuthorizeAsync</c>), consumers should inspect
/// <c>failure.FailedRequirements.OfType&lt;TenantRequirement&gt;()</c> directly — the
/// <c>g:tenant_required</c> discriminator is HTTP-pipeline-only.
/// </remarks>
internal sealed class TenantContextRequiredFeature;
