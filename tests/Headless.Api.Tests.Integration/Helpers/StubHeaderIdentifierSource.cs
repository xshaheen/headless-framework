// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace Tests.Helpers;

/// <summary>Test double reading one header as the raw tenant identifier; absent header yields None.</summary>
internal sealed class StubHeaderIdentifierSource(string headerName) : ITenantIdentifierSource
{
    public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
    {
        return context.Request.Headers.TryGetValue(headerName, out var values)
            ? TenantIdentifierSourceResult.Found(values.ToString())
            : TenantIdentifierSourceResult.None;
    }
}
