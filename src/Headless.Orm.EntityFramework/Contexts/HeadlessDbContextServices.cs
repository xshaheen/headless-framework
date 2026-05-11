// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

public sealed class HeadlessDbContextServices(
    ICurrentTenant currentTenant,
    IClock clock,
    IHeadlessSaveChangesPipeline saveChangesPipeline
)
{
    internal string? TenantId => currentTenant.Id;

    internal IClock Clock { get; } = clock;

    internal IHeadlessSaveChangesPipeline SaveChangesPipeline { get; } = saveChangesPipeline;
}
