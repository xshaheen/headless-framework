// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.AspNetCore.Mvc.Filters;

namespace Framework.Api.Mvc.Filters;

/// <summary>
/// Ensures that a HTTP request URL can contain query string parameters with both upper-case and lower-case
/// characters.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoLowercaseQueryStringAttribute : Attribute, IFilterMetadata;
