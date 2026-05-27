// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

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

    public HeadlessAuditLogSetupBuilder ConfigureStorage(Action<AuditLogStorageOptions> configure)
    {
        Argument.IsNotNull(configure);
        configure(StorageOptions);

        return this;
    }

    public void RegisterExtension(IAuditLogStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        Extensions.Add(extension);
    }
}
