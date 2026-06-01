// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

/// <summary>Setup-time extension hook for audit-log storage provider packages.</summary>
[PublicAPI]
public interface IAuditLogStorageOptionsExtension
{
    void AddServices(IServiceCollection services);
}
