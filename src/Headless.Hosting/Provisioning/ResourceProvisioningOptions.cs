// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Hosting;

/// <summary>
/// What an application can change about how its provisioned resources are managed. Configure it with
/// <c>services.ConfigureResourceProvisioning(options =&gt; ...)</c>.
/// </summary>
[PublicAPI]
public sealed class ResourceProvisioningOptions
{
    /// <summary>
    /// The environments where the operations that delete data (<c>resources purge</c>, <c>resources delete</c>,
    /// <see cref="ResourceStartupAction.ProvisionAndPurge" />, <c>PurgeResourcesAsync</c>, and
    /// <c>DeleteResourcesAsync</c>) run without <c>--force</c>. Compared case-insensitively. Every other environment
    /// refuses them, so a production-like environment with any name (<c>Prod</c>, <c>Live</c>, <c>Staging</c>) is safe by
    /// default. Default: <c>Development</c> and <c>Test</c>.
    /// </summary>
    public List<string> DestructiveEnvironments { get; set; } = [EnvironmentNames.Development, EnvironmentNames.Test];

    /// <summary>
    /// Overrides <see cref="IResourceProvisioner.StartupCheck" /> by <see cref="IResourceProvisioner.Kind" />, compared
    /// case-insensitively: <c>options.StartupChecks["RabbitMQ"] = ResourceStartupCheck.None</c> stops a host from
    /// checking its broker topology when it starts. A kind with no entry keeps the provisioner's own choice.
    /// </summary>
    public Dictionary<string, ResourceStartupCheck> StartupChecks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal ResourceStartupCheck StartupCheckOf(IResourceProvisioner provisioner) =>
        StartupChecks.TryGetValue(provisioner.Kind, out var check) ? check : provisioner.StartupCheck;
}

internal sealed class ResourceProvisioningOptionsValidator : AbstractValidator<ResourceProvisioningOptions>
{
    public ResourceProvisioningOptionsValidator()
    {
        RuleFor(x => x.DestructiveEnvironments).NotNull();
        RuleForEach(x => x.DestructiveEnvironments).NotEmpty();
        RuleFor(x => x.StartupChecks).NotNull();
        RuleForEach(x => x.StartupChecks).Must(static entry => Enum.IsDefined(entry.Value));
    }
}
