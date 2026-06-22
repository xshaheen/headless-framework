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
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoLowercaseQueryStringAttribute : Attribute, IFilterMetadata;
