// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// The registered modules, resolved from the container on first use, and the host policies each one requires.
/// </summary>
internal sealed class DashboardModuleCatalog(
    IServiceProvider services,
    DashboardRegistration registration,
    IOptionsMonitor<AuthConfig> auth
)
{
    private readonly Lazy<IReadOnlyList<IDashboardModule>> _modules = new(() => _Resolve(services, registration));

    /// <summary>The registered modules, in registration order.</summary>
    /// <exception cref="InvalidOperationException">A module's <see cref="IDashboardModule.Id"/> differs from its registered id.</exception>
    public IReadOnlyList<IDashboardModule> Modules => _modules.Value;

    /// <summary>
    /// Returns the view and operate policies the module requires under host authentication: the module's own, else the
    /// shell's. A <see langword="null"/> policy means the host's default policy.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No module is registered under <paramref name="moduleId"/>.</exception>
    public DashboardModulePolicy GetPolicy(string moduleId)
    {
        var module =
            registration.Modules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"No dashboard module is registered under '{moduleId}'.");

        var host = auth.Get(SetupDashboard.AuthenticationName).Host;
        var shellView = _NullIfEmpty(host.ViewPolicy);
        var shellOperate = _NullIfEmpty(host.OperatePolicy) ?? shellView;

        var options = module.ResolveOptions(services);
        var moduleView = _NullIfEmpty(options.ViewPolicy);
        var moduleOperate = _NullIfEmpty(options.OperatePolicy);

        return new DashboardModulePolicy
        {
            ViewPolicy = moduleView ?? shellView,
            OperatePolicy = moduleOperate ?? moduleView ?? shellOperate,
        };
    }

    private static IDashboardModule[] _Resolve(IServiceProvider services, DashboardRegistration registration)
    {
        var modules = new IDashboardModule[registration.Modules.Count];

        for (var i = 0; i < modules.Length; i++)
        {
            var descriptor = registration.Modules[i];
            var module = descriptor.ResolveModule(services);

            if (!string.Equals(module.Id, descriptor.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Dashboard module {module.GetType().Name} reports the id '{module.Id}' but was registered as "
                        + $"'{descriptor.Id}'. Register it under the id it reports."
                );
            }

            modules[i] = module;
        }

        return modules;
    }

    private static string? _NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>A module registered through <see cref="HeadlessDashboardSetupBuilder.AddModule{TModule, TOptions}"/>.</summary>
internal sealed record DashboardModuleDescriptor
{
    public required string Id { get; init; }

    public required Func<IServiceProvider, IDashboardModule> ResolveModule { get; init; }

    public required Func<IServiceProvider, DashboardModuleOptions> ResolveOptions { get; init; }
}

/// <summary>
/// The host policies a module requires under host authentication. A <see langword="null"/> policy means the host's
/// default policy.
/// </summary>
internal sealed record DashboardModulePolicy
{
    public required string? ViewPolicy { get; init; }

    public required string? OperatePolicy { get; init; }
}
