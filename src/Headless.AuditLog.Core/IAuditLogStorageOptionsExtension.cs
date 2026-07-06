// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

/// <summary>Setup-time extension hook for audit-log storage provider packages.</summary>
/// <remarks>
/// Provider packages implement this interface and register it with
/// <see cref="HeadlessAuditLogSetupBuilder.RegisterExtension"/> inside their <c>Use…</c>
/// extension method. <c>AddServices</c> is called once by the core setup pipeline after all
/// provider registrations have been collected.
/// </remarks>
[PublicAPI]
public interface IAuditLogStorageOptionsExtension
{
    /// <summary>
    /// Registers all provider-specific services (options, store, initializer, read log)
    /// into <paramref name="services"/>. Called once by the core setup pipeline.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    void AddServices(IServiceCollection services);
}
