// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

/// <summary>
/// Post-setup builder returned by <c>AddHeadlessAuditLog(setup =&gt; …)</c>.
/// Provides access to the <see cref="IServiceCollection"/> for further service registration after
/// the audit log and its storage provider have been configured.
/// </summary>
[PublicAPI]
public sealed class HeadlessAuditLogBuilder(IServiceCollection services)
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; } = services;
}
