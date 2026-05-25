// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

[PublicAPI]
public sealed class HeadlessAuditLogBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
