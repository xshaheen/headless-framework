// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc.Filters;

namespace Framework.Api.Filters;

/// <summary>
/// Ensures that a HTTP request URL can contain query string parameters with both upper-case and lower-case
/// characters.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoLowercaseQueryStringAttribute : Attribute, IFilterMetadata;
