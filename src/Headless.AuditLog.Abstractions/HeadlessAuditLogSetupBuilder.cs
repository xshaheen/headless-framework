// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

/// <summary>
/// Fluent builder for configuring the audit log and its storage provider during
/// <c>AddHeadlessAuditLog(setup =&gt; …)</c>. Accepts exactly one <c>Use…</c> extension call
/// (e.g., <c>UseEntityFramework</c>, <c>UsePostgreSql</c>, <c>UseSqlServer</c>) that selects
/// and wires the storage backend.
/// </summary>
[PublicAPI]
public sealed class HeadlessAuditLogSetupBuilder
{
    internal HeadlessAuditLogSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal AuditLogStorageOptions StorageOptions { get; } = new();

    // Compose configurators in registration order. Last-write-wins would silently drop earlier
    // calls, surprising consumers who split setup across extension methods or call ConfigureOptions
    // twice intentionally to layer overrides.
    internal Action<AuditLogOptions>? OptionsConfigurator { get; private set; }

    internal IList<IAuditLogStorageOptionsExtension> Extensions { get; } = new List<IAuditLogStorageOptionsExtension>();

    /// <summary>
    /// Configure the cross-cutting <see cref="AuditLogOptions"/> (capture strategy, etc.).
    /// Repeated calls compose: each registered delegate runs in registration order against the
    /// same <see cref="AuditLogOptions"/> instance, so later calls can override earlier values.
    /// </summary>
    public HeadlessAuditLogSetupBuilder ConfigureOptions(Action<AuditLogOptions> configure)
    {
        Argument.IsNotNull(configure);

        var previous = OptionsConfigurator;
        OptionsConfigurator = previous is null
            ? configure
            : options =>
            {
                previous(options);
                configure(options);
            };

        return this;
    }

    /// <summary>
    /// Configures shared storage options (schema, table name, JSON column type, <c>CreatedAt</c>
    /// column type, and startup initialization behavior). The delegate is applied immediately and
    /// merged into the shared <see cref="AuditLogStorageOptions"/> instance.
    /// </summary>
    /// <param name="configure">Delegate that mutates the shared storage options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessAuditLogSetupBuilder ConfigureStorage(Action<AuditLogStorageOptions> configure)
    {
        Argument.IsNotNull(configure);
        configure(StorageOptions);

        return this;
    }

    /// <summary>
    /// Registers a storage-provider extension that contributes services to the DI container
    /// when the setup builder is committed. Typically called by provider packages
    /// (e.g., <c>UseEntityFramework</c>, <c>UsePostgreSql</c>) rather than end consumers.
    /// </summary>
    /// <param name="extension">The extension to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public void RegisterExtension(IAuditLogStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        Extensions.Add(extension);
    }
}
