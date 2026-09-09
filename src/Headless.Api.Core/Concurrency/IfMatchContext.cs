// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Headless.Api.Concurrency;

internal sealed class IfMatchContext : IIfMatchContext
{
    public EntityTag? EntityTag { get; set; }

    public bool HasValue => EntityTag is not null;
}
