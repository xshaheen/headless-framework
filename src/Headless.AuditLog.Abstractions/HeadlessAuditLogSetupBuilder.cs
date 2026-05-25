// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
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

    internal Action<AuditLogOptions>? OptionsConfigurator { get; private set; }

    internal IList<IStorageOptionsExtension> Extensions { get; } = new List<IStorageOptionsExtension>();

    /// <summary>
    /// Configure the cross-cutting <see cref="AuditLogOptions"/> (capture strategy, etc.).
    /// </summary>
    public HeadlessAuditLogSetupBuilder ConfigureOptions(Action<AuditLogOptions> configure)
    {
        Argument.IsNotNull(configure);
        OptionsConfigurator = configure;

        return this;
    }

    public HeadlessAuditLogSetupBuilder ConfigureStorage(Action<AuditLogStorageOptions> configure)
    {
        Argument.IsNotNull(configure);
        configure(StorageOptions);

        return this;
    }

    public void RegisterExtension(IStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        Extensions.Add(extension);
    }
}
