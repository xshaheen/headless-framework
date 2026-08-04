// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc.Filters;

namespace Headless.Api.Filters;

/// <summary>
/// Marker attribute that opts an endpoint out of automatic query-string lowercasing applied by
/// <see cref="Headless.Api.Middlewares.RedirectToCanonicalUrlRule"/>. When present on a controller or action,
/// the rewrite rule preserves the original casing of query string parameters instead of redirecting
/// to a lowercase equivalent.
/// </summary>
/// <remarks>
/// Useful for endpoints whose query strings contain case-sensitive tokens (e.g. OAuth state parameters,
/// signed URLs, or legacy integration identifiers). Path lowercasing is unaffected by this attribute.
/// The rule reads this marker from endpoint metadata, so it only takes effect when the rule is registered
/// after <c>UseRouting()</c> — see <c>UseRedirectToCanonicalUrl()</c>. Before routing the rule cannot see the
/// marker and therefore performs no canonicalization at all rather than redirecting past the opt-out.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoLowercaseQueryStringAttribute : Attribute, IFilterMetadata;
